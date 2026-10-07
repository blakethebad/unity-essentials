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
    // Per-project IPC server: Neovim sends one-line commands asking Unity to refresh assets,
    // regenerate projects or drive play mode. Windows uses a named pipe, mac and linux a TCP
    // loopback port; either way the address is written to sync-pipe.txt for nvim to read.
    internal static class NeovimSyncServer
    {
        private const string PipePrefix = @"\\.\pipe\";
        private const string ServerPipeStem = "nvim-unity-sync-";
        private const string ClientPipeStem = "nvim-unity-client-";

        private static CancellationTokenSource _cts;
        private static Task _listenerTask;
        private static TcpListener _tcpListener; // non-Windows transport; null on Windows
        private static readonly ConcurrentQueue<string> _queue = new ConcurrentQueue<string>();
        // Open connections, tracked so Shutdown can dispose them on every domain reload
        // instead of leaving nvim writing into an orphaned listener.
        private static readonly ConcurrentDictionary<IDisposable, byte> _connections =
            new ConcurrentDictionary<IDisposable, byte>();
        private static IGenerator _generator;
        private static bool _loggedFirstError;
        private static string _projectRoot;

        // Exposed for NeovimAssetPostprocessor so asset moves/deletes can trigger a regen.
        internal static IGenerator ActiveGenerator => _generator;
        internal static string ProjectRoot => _projectRoot;

        // Swaps the generator while the listener is live. Start cannot: it early-returns
        // once the listener task is running.
        internal static void SetGenerator(IGenerator generator)
        {
            if (generator != null)
                _generator = generator;
        }

        [MenuItem("Tools/NvimUnity/Force SyncProject")]
        private static void ForceSyncProject()
        {
            if (_generator == null)
            {
                Debug.LogError("[NvimUnity] generator not initialized; reload the editor and try again.");
                return;
            }
            _generator.SyncProject();
        }

        // Idempotent. Safe to call from both the static ctor of NeovimScriptEditor and from Initialize.
        public static void Start(string projectRoot, IGenerator generator)
        {
            // AssetImportWorker child processes run InitializeOnLoad code too. Without this
            // guard one of them overwrites editor-pid.txt and serves the same pipe, so nvim
            // ends up talking to a headless worker. Also keeps the server out of CI runs.
            if (Application.isBatchMode) return;

            if (_listenerTask != null && !_listenerTask.IsCompleted) return;

            _projectRoot = projectRoot;
            _generator = generator;

            var libDir = Path.Combine(projectRoot, "Library", "NvimUnity");
            Directory.CreateDirectory(libDir);

            _cts = new CancellationTokenSource();
            _loggedFirstError = false;
            var token = _cts.Token;

            // Arm the listener before advertising the address, so it is live when nvim reads it.
            string advertised;
#if UNITY_EDITOR_WIN
            var pipeName = ServerPipeNameFor(projectRoot);
            advertised = pipeName;
            var localName = pipeName.Substring(PipePrefix.Length);
            _listenerTask = Task.Run(() => ListenLoop(localName, token));
#else
            // Unlike the hash-named pipe, the port changes on every Start. sync-pipe.txt is
            // rewritten below and nvim re-reads it per flush, so it always has the new one.
            _tcpListener = new TcpListener(IPAddress.Loopback, 0);
            _tcpListener.Start();
            advertised = "tcp:127.0.0.1:" + ((IPEndPoint)_tcpListener.LocalEndpoint).Port;
            var listener = _tcpListener;
            _listenerTask = Task.Run(() => TcpListenLoop(listener, token));
#endif

            // Drop the address on disk so the nvim lua side can read it without recomputing the hash.
            File.WriteAllText(Path.Combine(libDir, "sync-pipe.txt"), advertised);

            // Publish this editor's pid and debugger port so nvim's debugger can attach without
            // guessing. Rewritten on every domain reload, deleted only when the editor quits.
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

            // Quit only, not reload: deleting on reload would leave nvim with no file to find
            // the editor through while scripts recompile.
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
            // Release the port before the next domain arms its own listener; null on Windows.
            try { _tcpListener?.Stop(); } catch { }
            _tcpListener = null;
            // Drop every connection so nvim reconnects to the next domain's listener.
            // Disposing is also what unblocks the pending read.
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

                    // Read on a separate task and loop to arm the next instance, so one nvim
                    // cannot block a second one from connecting.
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

        // Background thread. Same shape as ListenLoop, for the TCP transport. Stopping the
        // listener is what unblocks a pending accept.
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

        // Background thread. Queues one connection's lines. `owner` is the connection object;
        // disposing it tears the stream down.
        private static async Task ReadConnection(IDisposable owner, Stream stream, CancellationToken ct)
        {
            _connections.TryAdd(owner, 0);
            // The read cannot be cancelled, so dispose the stream to unblock an idle connection.
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

        // Main thread, where Unity API calls are legal. Collects the whole queue first and then
        // acts once, so a burst of nvim events costs one refresh.
        private const int ImportThreshold = 25;

        private static void DrainQueue()
        {
            if (_queue.IsEmpty) return;

            bool fullRefresh = false;
            bool doSync = false;
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
                        // Retired, but still accepted so an older nvim degrades gracefully.
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
                        // A verb this package does not know means a newer nvim plugin, not a user error.
                        break;
                }
            }

            // Whole drain body guarded so a single bad message can't throw out of EditorApplication.update.
            try
            {
                if (fullRefresh || (importPaths != null && importPaths.Count > ImportThreshold))
                {
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
                            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                        }
                        else
                        {
                            needFullRefresh = true;
                        }
                    }
                    if (needFullRefresh)
                    {
                        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    }
                }

                if (doSync)
                {
                    _generator?.SyncProject();
                }

                if (playState.HasValue)
                {
                    EditorApplication.isPlaying = playState.Value;
                }

                // After the play block, so a play+pause batch starts play mode already paused.
                if (pauseState.HasValue)
                {
                    // isPlaying only changes on a later update, so the getter still reports the
                    // old state here. Trust what this batch just asked for, if it asked at all.
                    var effectivePlaying = playState.HasValue ? playState.Value : EditorApplication.isPlaying;
                    if (pauseState.Value)
                    {
                        // Pausing in edit mode only arms the pause button, which would make the
                        // next play session start paused. A play+pause batch wants exactly that.
                        if (effectivePlaying)
                            EditorApplication.isPaused = true;
                    }
                    else
                    {
                        // Unconditional: also disarms a pause armed while not playing.
                        EditorApplication.isPaused = false;
                    }
                }

                // Debugger recovery (DebuggerRecovery.cs). Each call catches its own errors and
                // moves socket work off this thread itself.
                if (dbgStatus)   DebuggerRecovery.RunStatusProbe(_projectRoot);
                if (dbgRecover1) DebuggerRecovery.Recover1_Disconnect(_projectRoot);
                if (dbgRecover2) DebuggerRecovery.Recover2_CodeOptimizationFlip(_projectRoot);
                if (dbgRecover3) DebuggerRecovery.Recover3_RestartEditor(_projectRoot);
            }
            catch (Exception ex)
            {
                Debug.LogError("[NvimUnity] drain failed: " + ex.Message);
            }
        }

        private static void LogOnce(string msg)
        {
            if (_loggedFirstError) return;
            _loggedFirstError = true;
            Debug.LogError("[NvimUnity] sync server: " + msg + " (further errors suppressed)");
        }

        // Unity's Mono debugger listens on 127.0.0.1:(56000 + pid % 1000). Undocumented but
        // stable, and this is the only place the formula lives.
        internal static int DebuggerPortForCurrentProcess()
        {
            var pid = System.Diagnostics.Process.GetCurrentProcess().Id;
            return 56000 + (pid % 1000);
        }

        public static string ServerPipeNameFor(string projectRoot)
        {
            return PipePrefix + ServerPipeStem + HashProjectRoot(projectRoot);
        }

        // The address we tell nvim to --listen on so we can --remote-send to it later.
        // A named pipe on Windows, a unix socket path under the temp dir elsewhere.
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
