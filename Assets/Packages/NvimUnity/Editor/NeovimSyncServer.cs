using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace NvimUnity.Editor
{
    // Hosts a per-project IPC server so Neovim (via lua autocmds) can ask Unity to
    // refresh the AssetDatabase or regenerate projects after .cs file events on disk.
    // Transport is platform-branched (MACOS_PLAN §2.2 in the nvim config repo):
    // Windows keeps the named pipe (byte-identical wire + advertise format); mac/linux
    // listen on TCP loopback (managed named pipes are researched-unreliable in Unity's
    // Mono there) and advertise the self-describing `tcp:127.0.0.1:<port>` form in
    // sync-pipe.txt, which the nvim side parses to pick its transport.
    internal static class NeovimSyncServer
    {
        private const string PipePrefix = @"\\.\pipe\";
        private const string ServerPipeStem = "nvim-unity-sync-";
        private const string ClientPipeStem = "nvim-unity-client-";

        private const string LogPrefKey = "NvimUnity.VerboseLog";

        private static CancellationTokenSource _cts;
        private static Task _listenerTask;
        private static TcpListener _tcpListener; // non-Windows transport; null on Windows
        private static readonly ConcurrentQueue<string> _queue = new ConcurrentQueue<string>();
        // Live per-connection readers (NamedPipeServerStream or TcpClient). Tracked so
        // Shutdown (fires on every beforeAssemblyReload / recompile) can dispose them
        // and break any persistent nvim connection deterministically, instead of
        // orphaning it across the domain reload while the new domain arms a fresh,
        // unconnected listener instance.
        private static readonly ConcurrentDictionary<IDisposable, byte> _connections =
            new ConcurrentDictionary<IDisposable, byte>();
        private static IGenerator _generator;
        private static bool _loggedFirstError;
        private static string _projectRoot;

        public static bool VerboseLog
        {
            get { return EditorPrefs.GetBool(LogPrefKey, false); }
            set { EditorPrefs.SetBool(LogPrefKey, value); }
        }

        // Exposed for NeovimAssetPostprocessor so asset moves/deletes can trigger a regen.
        internal static IGenerator ActiveGenerator => _generator;
        internal static string ProjectRoot => _projectRoot;

        // Swap the generator behind a LIVE listener. Start cannot do this: it
        // early-returns while the listener task runs, deliberately, so calling it
        // again never reassigns _generator. Used by the Preferences dropdown
        // (NeovimScriptEditor.SwapGenerator).
        internal static void SetGenerator(IGenerator generator)
        {
            if (generator != null)
                _generator = generator;
        }

        private static void VLog(string msg)
        {
            if (!VerboseLog) return;
            Debug.Log("[NvimUnity] " + msg);
        }

        [MenuItem("Tools/NvimUnity/Toggle Verbose Log")]
        private static void ToggleVerboseLog()
        {
            VerboseLog = !VerboseLog;
            Debug.Log("[NvimUnity] verbose log " + (VerboseLog ? "ON" : "OFF"));
        }

        [MenuItem("Tools/NvimUnity/Toggle Verbose Log", true)]
        private static bool ToggleVerboseLogValidate()
        {
            Menu.SetChecked("Tools/NvimUnity/Toggle Verbose Log", VerboseLog);
            return true;
        }

        [MenuItem("Tools/NvimUnity/Force SyncProject")]
        private static void ForceSyncProject()
        {
            if (_generator == null)
            {
                Debug.LogWarning("[NvimUnity] generator not initialized; reload the editor and try again.");
                return;
            }
            _generator.SyncProject();
        }

        // Idempotent. Safe to call from both the static ctor of NeovimScriptEditor and from Initialize.
        public static void Start(string projectRoot, IGenerator generator)
        {
            // AssetImportWorker child processes (Unity.exe -batchMode -name AssetImportWorkerN)
            // run [InitializeOnLoad] editor code too. Without this guard a worker OVERWRITES
            // editor-pid.txt with ITS pid/debugger-port (nvim then attaches its debugger to the
            // headless worker — handshake succeeds but user scripts never execute there, so
            // breakpoints stay NotBound/hollow) and serves a second instance of the SAME-named
            // sync pipe (play/refresh verbs randomly land in the worker). Verified live
            // 2026-07-19: editor-pid.txt held AssetImportWorker0's pid. isBatchMode also
            // correctly keeps the server out of CI/batch runs.
            if (Application.isBatchMode) return;

            if (_listenerTask != null && !_listenerTask.IsCompleted) return;

            _projectRoot = projectRoot;
            _generator = generator;

            var libDir = Path.Combine(projectRoot, "Library", "NvimUnity");
            Directory.CreateDirectory(libDir);

            _cts = new CancellationTokenSource();
            _loggedFirstError = false;
            var token = _cts.Token;

            // Platform-branched transport (see the class header). The listener is armed
            // BEFORE the advertise write so the advertised address is always live.
            string advertised;
#if UNITY_EDITOR_WIN
            var pipeName = ServerPipeNameFor(projectRoot);
            advertised = pipeName;
            var localName = pipeName.Substring(PipePrefix.Length);
            _listenerTask = Task.Run(() => ListenLoop(localName, token));
#else
            // Ephemeral port: unlike the stable hash-named pipe it CHANGES on every
            // Start (each domain reload). sync-pipe.txt is rewritten below each time
            // and nvim re-reads it per flush, so the fresh port is always advertised;
            // nvim's drop-on-failed-connect covers the mid-recompile gap.
            _tcpListener = new TcpListener(IPAddress.Loopback, 0);
            _tcpListener.Start();
            advertised = "tcp:127.0.0.1:" + ((IPEndPoint)_tcpListener.LocalEndpoint).Port;
            var listener = _tcpListener;
            _listenerTask = Task.Run(() => TcpListenLoop(listener, token));
#endif

            // Drop the address on disk so the nvim lua side can read it without recomputing the hash.
            File.WriteAllText(Path.Combine(libDir, "sync-pipe.txt"), advertised);

            // Advertise this editor's pid + derived Mono debugger port so the nvim DAP side can
            // attach without guessing. Rewritten after every domain reload (Start re-runs), so
            // it stays fresh; deleted only on a real editor quit (see DeleteEditorPidFile).
            var pid = System.Diagnostics.Process.GetCurrentProcess().Id;
            var debuggerPort = DebuggerPortForCurrentProcess();
            File.WriteAllText(
                Path.Combine(libDir, "editor-pid.txt"),
                "{\"process_id\":" + pid + ",\"debugger_port\":" + debuggerPort + "}");

            EditorApplication.update -= DrainQueue;
            EditorApplication.update += DrainQueue;

            AssemblyReloadEvents.beforeAssemblyReload -= Shutdown;
            AssemblyReloadEvents.beforeAssemblyReload += Shutdown;

            EditorApplication.quitting -= Shutdown;
            EditorApplication.quitting += Shutdown;

            // quitting only, NOT beforeAssemblyReload: Start rewrites the file after each
            // reload, and deleting it on reload would leave nvim a mid-recompile gap with
            // no file to discover the editor from.
            EditorApplication.quitting -= DeleteEditorPidFile;
            EditorApplication.quitting += DeleteEditorPidFile;
        }

        private static void DeleteEditorPidFile()
        {
            try
            {
                var path = Path.Combine(_projectRoot ?? string.Empty, "Library", "NvimUnity", "editor-pid.txt");
                if (File.Exists(path)) File.Delete(path);
            }
            catch { /* best effort; nvim validates the pid is alive anyway */ }
        }

        private static void Shutdown()
        {
            try { _cts?.Cancel(); } catch { }
            // Stop the TCP listener (non-Windows transport) so the ephemeral port is
            // released before the next domain arms a fresh one; null no-op on Windows.
            try { _tcpListener?.Stop(); } catch { }
            _tcpListener = null;
            // Break every outstanding connection so a persistent nvim client is forced to
            // reconnect to the next domain's fresh listener instead of writing into an
            // orphaned pipe. Disposing also unblocks the pending ReadLineAsync (which takes
            // no CancellationToken on .NET 4.7.1).
            foreach (var kv in _connections)
            {
                try { kv.Key.Dispose(); } catch { }
            }
            _connections.Clear();
            EditorApplication.update -= DrainQueue;
            _listenerTask = null;
        }

        // Background thread. No Unity API calls allowed here.
        private static async Task ListenLoop(string localName, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                NamedPipeServerStream server = null;
                try
                {
                    server = new NamedPipeServerStream(
                        localName,
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(ct).ConfigureAwait(false);

                    // Hand the connected stream to its own reader task and immediately loop to
                    // arm the next server instance, so a persistent nvim connection can't starve
                    // a second nvim instance from connecting.
                    var connected = server;
                    server = null;
                    _ = Task.Run(() => ReadConnection(connected, connected, ct));
                }
                catch (OperationCanceledException) { break; }
                catch (IOException ioEx)         { LogOnce("pipe IO: " + ioEx.Message); Thread.Sleep(500); }
                catch (ObjectDisposedException)  { /* expected on shutdown */ }
                catch (Exception ex)             { LogOnce("pipe error: " + ex.Message); Thread.Sleep(500); }
                finally { server?.Dispose(); }
            }
        }

        // Background thread. Accept loop for the TCP transport (non-Windows). Mirrors
        // ListenLoop's shape: accept → per-connection reader task → next accept.
        // AcceptTcpClientAsync takes no CancellationToken on .NET 4.7.1; stopping the
        // listener (Shutdown does it too) unblocks the pending accept.
        private static async Task TcpListenLoop(TcpListener listener, CancellationToken ct)
        {
            using (ct.Register(() => { try { listener.Stop(); } catch { } }))
            {
                while (!ct.IsCancellationRequested)
                {
                    TcpClient client = null;
                    try
                    {
                        client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                        var connected = client;
                        client = null;
                        _ = Task.Run(() => ReadConnection(connected, connected.GetStream(), ct));
                    }
                    catch (OperationCanceledException) { break; }
                    catch (ObjectDisposedException)  { break; } // listener stopped on shutdown/reload
                    catch (Exception ex)
                    {
                        if (ct.IsCancellationRequested) break;
                        LogOnce("tcp error: " + ex.Message);
                        Thread.Sleep(500);
                    }
                    finally { client?.Dispose(); }
                }
            }
        }

        // Background thread. Drains one connection's lines into the queue. `owner` is the
        // disposable connection object tracked in _connections (the NamedPipeServerStream
        // itself, or the TcpClient wrapping `stream`) — disposing it tears the stream down.
        private static async Task ReadConnection(IDisposable owner, Stream stream, CancellationToken ct)
        {
            _connections.TryAdd(owner, 0);
            // ReadLineAsync takes no CancellationToken on .NET 4.7.1, so tear the stream down on
            // cancellation to unblock a persistent, idle nvim connection during shutdown/reload.
            using (ct.Register(() => { try { owner.Dispose(); } catch { } }))
            {
                try
                {
                    using (owner)
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        string line;
                        while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                        {
                            if (ct.IsCancellationRequested) break;
                            _queue.Enqueue(line.Trim());
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (ObjectDisposedException) { }
                catch (IOException) { /* stream disposed mid-read on shutdown */ }
                catch (Exception ex) { LogOnce("read: " + ex.Message); }
                finally
                {
                    byte ignored;
                    _connections.TryRemove(owner, out ignored);
                }
            }
        }

        // Main thread. Unity API calls happen here. Drains the whole queue into locals first,
        // then acts once so a burst of nvim events costs at most one refresh (or N imports).
        private const int ImportThreshold = 25;

        private static void DrainQueue()
        {
            if (_queue.IsEmpty) return;

            bool fullRefresh = false;
            bool doSync = false;
            bool deprecatedRearm = false; // retired verb; log once per batch (see below)
            bool dbgStatus = false;   // wedge probe
            bool dbgRecover1 = false; // ManagedDebugger.Disconnect()
            bool dbgRecover2 = false; // codeOptimization double-flip
            bool dbgRecover3 = false; // scripted editor restart
            bool? playState = null;  // last play/stop in the batch wins
            bool? pauseState = null; // last pause/resume in the batch wins
            HashSet<string> importPaths = null;

            while (_queue.TryDequeue(out var msg))
            {
                if (string.IsNullOrEmpty(msg)) continue;
                var space = msg.IndexOf(' ');
                var verb = space < 0 ? msg : msg.Substring(0, space);
                var arg  = space < 0 ? null : msg.Substring(space + 1);
                switch (verb)
                {
                    case "refresh":
                        if (string.IsNullOrEmpty(arg))
                        {
                            fullRefresh = true;
                        }
                        else if (!fullRefresh)
                        {
                            // nvim sends forward slashes; normalize defensively for AssetDatabase.
                            if (importPaths == null)
                                importPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            importPaths.Add(arg.Replace('\\', '/'));
                        }
                        break;
                    case "sync":
                        doSync = true;
                        break;
                    case "play":
                        playState = true;
                        break;
                    case "stop":
                        playState = false;
                        break;
                    case "pause":
                        pauseState = true;
                        break;
                    case "resume":
                        pauseState = false;
                        break;
                    case "rearm":
                        // RETIRED (see the deprecation block below). Label kept for wire
                        // compatibility so an old nvim writing `rearm` degrades gracefully.
                        deprecatedRearm = true;
                        break;
                    case "dbgstatus":
                        dbgStatus = true;
                        break;
                    case "dbgrecover1":
                        dbgRecover1 = true;
                        break;
                    case "dbgrecover2":
                        dbgRecover2 = true;
                        break;
                    case "dbgrecover3":
                        dbgRecover3 = true;
                        break;
                    default:
                        Debug.LogWarning("[NvimUnity] unknown IPC message: " + msg);
                        break;
                }
            }

            // Whole drain body guarded so a single bad message can't throw out of EditorApplication.update.
            try
            {
                if (fullRefresh || (importPaths != null && importPaths.Count > ImportThreshold))
                {
                    VLog("DrainQueue: full AssetDatabase.Refresh(ForceSynchronousImport)");
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                }
                else if (importPaths != null && importPaths.Count > 0)
                {
                    bool needFullRefresh = false;
                    foreach (var path in importPaths)
                    {
                        var abs = Path.Combine(_projectRoot ?? string.Empty, path);
                        if (File.Exists(abs))
                        {
                            VLog("DrainQueue: ImportAsset " + path);
                            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                        }
                        else
                        {
                            needFullRefresh = true;
                        }
                    }
                    if (needFullRefresh)
                    {
                        VLog("DrainQueue: invalid import path(s) -> full refresh fallback");
                        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    }
                }

                if (doSync)
                {
                    VLog("DrainQueue: SyncProject");
                    _generator?.SyncProject();
                }

                if (playState.HasValue)
                {
                    VLog("DrainQueue: EditorApplication.isPlaying = " + playState.Value);
                    EditorApplication.isPlaying = playState.Value;
                }

                // After the playState block, so a "play" + "pause" batch enters play
                // mode already paused (nvim emits play/stop before pause/resume).
                if (pauseState.HasValue)
                {
                    // Setting isPlaying is DEFERRED (takes effect on a later update), so
                    // right after the playState block the isPlaying getter still reports
                    // the old state. For the play+pause (or stop+pause) batch case the
                    // just-requested playState is therefore the effective one; without a
                    // playState in the batch, the live isPlaying is authoritative.
                    var effectivePlaying = playState.HasValue ? playState.Value : EditorApplication.isPlaying;
                    if (pauseState.Value)
                    {
                        // Playing guard: setting isPaused in edit mode arms the pause
                        // button, which would make the NEXT play session start paused.
                        // (In the play+pause batch, arming it is exactly what makes the
                        // deferred play session begin paused — so that case passes.)
                        if (effectivePlaying)
                        {
                            VLog("DrainQueue: EditorApplication.isPaused = true");
                            EditorApplication.isPaused = true;
                        }
                        else
                        {
                            VLog("DrainQueue: pause ignored (editor not in play mode)");
                        }
                    }
                    else
                    {
                        // Unconditional: also disarms a pause armed while not playing.
                        VLog("DrainQueue: EditorApplication.isPaused = false");
                        EditorApplication.isPaused = false;
                    }
                }

                // RETIRED `rearm` verb. It used to toggle the AllowAttachedDebuggingOfEditor
                // EditorPref off/on as a supposed programmatic version of Preferences ->
                // External Tools -> "Editor Attaching". That pref is plausibly startup-read-only
                // on 2019.3+ (the operative machinery is ScriptDebugInfoEnabled/codeOptimization),
                // and it was never confirmed to actually re-arm the wedged Mono agent. The wedge
                // recovery ladder now lives in DebuggerRecovery (dbgstatus / dbgrecover1-3).
                if (deprecatedRearm)
                {
                    Debug.LogWarning("[NvimUnity] `rearm` is retired and does nothing — use "
                        + "dbgstatus to probe the debugger agent and dbgrecover1/dbgrecover2/dbgrecover3 to recover it.");
                }

                // Debugger-agent wedge recovery (see DebuggerRecovery.cs). Each entry point is
                // self-contained + try/caught internally, so a throw can't skip a sibling verb.
                // Socket work is offloaded to a background thread inside these calls; only the
                // main-thread-only bits (ManagedDebugger, codeOptimization, restart) run here.
                if (dbgStatus)   DebuggerRecovery.RunStatusProbe(_projectRoot);
                if (dbgRecover1) DebuggerRecovery.Recover1_Disconnect(_projectRoot);
                if (dbgRecover2) DebuggerRecovery.Recover2_CodeOptimizationFlip(_projectRoot);
                if (dbgRecover3) DebuggerRecovery.Recover3_RestartEditor(_projectRoot);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[NvimUnity] drain failed: " + ex.Message);
            }
        }

        private static void LogOnce(string msg)
        {
            if (_loggedFirstError) return;
            _loggedFirstError = true;
            Debug.LogWarning("[NvimUnity] sync server: " + msg + " (further errors suppressed)");
        }

        // Unity's embedded Mono soft-debugger agent listens on
        // 127.0.0.1:(56000 + pid % 1000). This is the SINGLE source of that formula:
        // Start writes it into editor-pid.txt for the nvim DAP side, and
        // DebuggerRecovery probes the same port when checking/recovering the agent.
        // Undocumented-but-stable Unity behavior (see the DAP fragility watchlist).
        internal static int DebuggerPortForCurrentProcess()
        {
            var pid = System.Diagnostics.Process.GetCurrentProcess().Id;
            return 56000 + (pid % 1000);
        }

        public static string ServerPipeNameFor(string projectRoot)
        {
            return PipePrefix + ServerPipeStem + HashProjectRoot(projectRoot);
        }

        // Per-project client address that we tell nvim to --listen on, so we can --remote-send
        // to it later. Windows: named pipe. mac/linux: a unix-socket path under the temp dir
        // (`nvim --listen` accepts a socket path there — MACOS_PLAN U3).
        public static string DefaultClientPipeNameFor(string projectRoot)
        {
#if UNITY_EDITOR_WIN
            return PipePrefix + ClientPipeStem + HashProjectRoot(projectRoot);
#else
            return Path.Combine(Path.GetTempPath(), ClientPipeStem + HashProjectRoot(projectRoot));
#endif
        }

        private static string HashProjectRoot(string projectRoot)
        {
            var norm = Path.GetFullPath(projectRoot).Replace('/', '\\').ToLowerInvariant();
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(norm));
                var sb = new StringBuilder(12);
                for (int i = 0; i < 6; i++) sb.AppendFormat("{0:x2}", hash[i]);
                return sb.ToString();
            }
        }
    }
}
