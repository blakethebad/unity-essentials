using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace NvimUnity.Editor
{
    // Wedge-recovery tooling for Unity's embedded Mono soft-debugger agent.
    //
    // WHY this exists: the agent performs its 13-byte "DWP-Handshake" (agent-first;
    // the client echoes it) exactly once per accept. After an imperfect client detach
    // nothing re-services the listen socket, yet the kernel keeps completing TCP
    // handshakes into the listen backlog — so a later attach connects but never gets a
    // handshake and no breakpoint ever binds, for the rest of the editor process. That
    // "wedge" is invisible at the wire without probing, and used to force an editor
    // restart. This module (a) probes the agent's own port to classify it and (b)
    // offers a graduated recovery ladder, all driven by newline verbs over the existing
    // sync pipe (dispatched from NeovimSyncServer.DrainQueue on the main thread):
    //
    //   dbgstatus    -> classify: armed | wedged | attached | no-listener
    //   dbgrecover1  -> ManagedDebugger.Disconnect() (cheapest; no recompile)
    //   dbgrecover2  -> CompilationPipeline.codeOptimization double-flip (2 recompiles)
    //   dbgrecover3  -> scripted editor restart (guaranteed, heaviest)
    //
    // Results are reported two ways for the nvim side: Debug.Log (with the shared
    // "[NvimUnity]" prefix) and a single-line JSON status file at
    // <root>/Library/NvimUnity/dbg-status.txt (same dir as editor-pid.txt).
    //
    // API note: several editor entry points used here are not guaranteed stable across
    // Unity versions (ManagedDebugger's exact member surface;
    // RequestCloseAndRelaunchWithCurrentArguments is internal). Those are reached via
    // reflection with a logged fallback so a missing member degrades to a clear warning
    // instead of breaking compilation of the whole NvimUnity package.
    internal static class DebuggerRecovery
    {
        private const string StatusFileName = "dbg-status.txt";

        // ---- SessionState keys for the recover2 domain-reload continuation ----
        // SessionState survives domain reloads within one editor session and is cleared
        // on editor quit — exactly the lifetime the codeOptimization double-flip needs.
        private const string Recover2StageKey = "NvimUnity.DbgRecover2.Stage";
        private const string Recover2RootKey  = "NvimUnity.DbgRecover2.Root";
        private const string StageSetDebug = "SetDebug"; // after this reload: flip to Debug
        private const string StageProbe    = "Probe";    // after this reload: re-probe + finish

        // Mirrors NeovimSyncServer.VLog gating without depending on its private method.
        private static void VLog(string msg)
        {
            if (!NeovimSyncServer.VerboseLog) return;
            Debug.Log("[NvimUnity] " + msg);
        }

        // =====================================================================
        // dbgstatus — classify the debugger agent (main-thread entry)
        // =====================================================================
        //
        // WHY: distinguishing "wedged" from "busy" requires the ManagedDebugger.isAttached
        // gate FIRST (on the main thread): the agent is single-client, so while a debugger
        // is attached a second connect just sits in the backlog unserviced — wire-identical
        // to a wedge. Only when nothing is attached do we probe the socket, and that probe
        // is offloaded to a background thread (it blocks up to ~4s) per the house rule that
        // DrainQueue/update callbacks never block on I/O.
        public static void RunStatusProbe(string root)
        {
            try
            {
                int port = NeovimSyncServer.DebuggerPortForCurrentProcess();

                // Main-thread attach check: busy != wedged.
                bool attached;
                if (TryGetDebuggerAttached(out attached) && attached)
                {
                    WriteAndLogStatus(root, "attached", port);
                    return;
                }

                // Not attached -> safe to probe. Socket work off the main thread.
                // VerboseLog reads EditorPrefs (main-thread-only) — capture it HERE and
                // pass it in; the probe thread must never touch editor state.
                bool verbose = NeovimSyncServer.VerboseLog;
                Task.Run(() =>
                {
                    string status;
                    try { status = ProbeAgentSocket(port, verbose); }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[NvimUnity] dbgstatus: probe thread failed: " + ex.Message);
                        status = "error";
                    }
                    WriteAndLogStatus(root, status, port);
                });
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[NvimUnity] dbgstatus failed: " + ex.Message);
            }
        }

        // Background thread. Connects to the agent's own loopback port and classifies it.
        //   connect refused/timeout          -> "no-listener"
        //   connected, no 13 bytes in 3s     -> "wedged"
        //   connected, 13 handshake bytes    -> echo them + send VM_DISPOSE + close -> "armed"
        // A completed probe IS a perfectly clean detach, so it leaves the agent re-armed.
        private static string ProbeAgentSocket(int port, bool verbose)
        {
            TcpClient client = null;
            try
            {
                client = new TcpClient();
                // .NET 4.7.1: no connect-timeout overload; BeginConnect + WaitOne is the idiom.
                var ar = client.BeginConnect(IPAddress.Loopback, port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(1000))
                {
                    if (verbose) Debug.Log("[NvimUnity] dbgstatus: connect timed out -> no-listener");
                    return "no-listener";
                }
                try
                {
                    client.EndConnect(ar);
                }
                catch (SocketException)
                {
                    // Connection refused (nothing listening on the port).
                    if (verbose) Debug.Log("[NvimUnity] dbgstatus: connect refused -> no-listener");
                    return "no-listener";
                }

                var stream = client.GetStream();
                stream.ReadTimeout = 3000;

                // Agent sends the 13-byte handshake FIRST. Read exactly 13 (or time out).
                byte[] handshake = new byte[13];
                int read = 0;
                try
                {
                    while (read < handshake.Length)
                    {
                        int n = stream.Read(handshake, read, handshake.Length - read);
                        if (n <= 0) break; // peer closed
                        read += n;
                    }
                }
                catch (IOException)
                {
                    // ReadTimeout elapsed with no (or partial) handshake.
                }

                if (read < handshake.Length)
                {
                    if (verbose) Debug.Log("[NvimUnity] dbgstatus: connected but no handshake (" + read + "/13 bytes) -> wedged");
                    return "wedged";
                }

                // Handshake received: echo it back, then a clean VM_DISPOSE. This is the
                // perfectly clean detach that leaves the agent re-armed for the next attach.
                stream.Write(handshake, 0, handshake.Length);
                stream.Flush();
                byte[] dispose = BuildVmDispose();
                stream.Write(dispose, 0, dispose.Length);
                stream.Flush();
                if (verbose) Debug.Log("[NvimUnity] dbgstatus: handshake ok, sent VM_DISPOSE -> armed");
                return "armed";
            }
            catch (SocketException)
            {
                return "no-listener";
            }
            finally
            {
                try { if (client != null) client.Close(); } catch { }
            }
        }

        // VM_DISPOSE command packet: 11-byte big-endian header, no data.
        //   length=0x0000000B, id (any int), flags=0x00, command_set=0x01 (VM), command=0x06 (DISPOSE)
        private static byte[] BuildVmDispose()
        {
            return new byte[]
            {
                0x00, 0x00, 0x00, 0x0B, // length = 11
                0x00, 0x00, 0x00, 0x01, // id = 1 (any)
                0x00,                   // flags
                0x01,                   // command_set = VM
                0x06,                   // command = DISPOSE
            };
        }

        // Writes the single-line status JSON to <root>/Library/NvimUnity/dbg-status.txt and
        // logs it. Safe to call from the probe's background thread: File I/O is thread-safe
        // and UnityEngine.Debug.Log marshals to the main thread internally.
        private static void WriteAndLogStatus(string root, string status, int port)
        {
            string iso = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            string json = "{\"status\":\"" + status + "\",\"checked_at\":\"" + iso + "\",\"port\":" + port + "}";
            try
            {
                var dir = Path.Combine(root ?? string.Empty, "Library", "NvimUnity");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, StatusFileName), json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[NvimUnity] dbgstatus: could not write status file: " + ex.Message);
            }
            Debug.Log("[NvimUnity] dbgstatus: " + json);
        }

        // =====================================================================
        // dbgrecover1 — ManagedDebugger.Disconnect() (main-thread entry)
        // =====================================================================
        //
        // WHY (cheapest rung): Unity's Mono fork exposes mono_debugger_disconnect(), which
        // stops the debugger thread and resets agent_inited so the agent can re-init. The
        // hypothesis is that the public ManagedDebugger.Disconnect() (the editor status-bar
        // debugger UI is built on it) routes there and cleanly re-arms the agent with no
        // recompile. UNVERIFIED as a wedge fix — hence the auto re-probe afterward.
        public static void Recover1_Disconnect(string root)
        {
            try
            {
                bool before;
                bool haveBefore = TryGetDebuggerAttached(out before);
                Debug.Log("[NvimUnity] dbgrecover1: calling ManagedDebugger.Disconnect() (isAttached before="
                    + (haveBefore ? before.ToString() : "unknown") + ")");

                var t = ResolveManagedDebuggerType();
                if (t == null)
                {
                    Debug.LogWarning("[NvimUnity] dbgrecover1: ManagedDebugger type not found; cannot Disconnect()");
                }
                else
                {
                    var mi = t.GetMethod("Disconnect",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                        null, Type.EmptyTypes, null);
                    if (mi == null)
                    {
                        Debug.LogWarning("[NvimUnity] dbgrecover1: ManagedDebugger.Disconnect() not found (API surface differs)");
                    }
                    else
                    {
                        mi.Invoke(null, null);
                        bool after;
                        bool haveAfter = TryGetDebuggerAttached(out after);
                        Debug.Log("[NvimUnity] dbgrecover1: Disconnect() returned (isAttached after="
                            + (haveAfter ? after.ToString() : "unknown") + ")");
                    }
                }

                // Re-probe ~1s later so the result reflects the post-Disconnect state.
                ScheduleMainThread(1.0, () => RunStatusProbe(root));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[NvimUnity] dbgrecover1 failed: " + ex.Message);
            }
        }

        // =====================================================================
        // dbgrecover2 — codeOptimization double-flip (main-thread entry)
        // =====================================================================
        //
        // WHY (middle rung): toggling CompilationPipeline.codeOptimization is the same
        // machinery as the status-bar Debug button and must touch the agent live — flipping
        // to Debug makes editor attach possible without a restart. A double-flip
        // (Debug -> Release -> Debug) forces a full rebind. Cost: TWO recompile + domain
        // reload cycles, so the driver MUST survive reloads. State lives in SessionState and
        // the continuation runs from [InitializeOnLoadMethod] (Recover2Continuation below).
        // Guard: if already Release, a single Release -> Debug flip suffices.
        public static void Recover2_CodeOptimizationFlip(string root)
        {
            try
            {
                var current = CompilationPipeline.codeOptimization;
                SessionState.SetString(Recover2RootKey, root ?? string.Empty);

                if (current == CodeOptimization.Release)
                {
                    Debug.Log("[NvimUnity] dbgrecover2: codeOptimization already Release — single flip Release->Debug (1 recompile)");
                    SessionState.SetString(Recover2StageKey, StageProbe);
                    SetCodeOptimization(CodeOptimization.Debug, "rung 2 (single flip): Debug");
                }
                else
                {
                    Debug.Log("[NvimUnity] dbgrecover2: double-flip Debug->Release->Debug (2 recompiles, survives domain reloads)");
                    SessionState.SetString(Recover2StageKey, StageSetDebug);
                    SetCodeOptimization(CodeOptimization.Release, "rung 2 (1/2): Release");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[NvimUnity] dbgrecover2 failed: " + ex.Message);
            }
        }

        // Runs on every domain load. Drives the recover2 flip sequence across the recompiles
        // it itself triggers. No-op unless a flip is in progress (SessionState stage set).
        //
        // Deferral is an EditorApplication.update poll (ScheduleMainThread), NOT delayCall:
        // a delayCall registered here was observed to be silently dropped after the second
        // reload (live, 2026-07-17) — the stage-2 flip applied but the final probe never ran.
        // update callbacks are pumped unconditionally, so the poll cannot be lost.
        [InitializeOnLoadMethod]
        private static void Recover2Continuation()
        {
            string stage = SessionState.GetString(Recover2StageKey, string.Empty);
            if (string.IsNullOrEmpty(stage)) return;

            string root = SessionState.GetString(Recover2RootKey, string.Empty);
            Debug.Log("[NvimUnity] dbgrecover2 continuation: stage=" + stage);

            ScheduleMainThread(1.0, () =>
            {
                try
                {
                    if (stage == StageSetDebug)
                    {
                        // First reload complete (now in Release); flip back to Debug.
                        SessionState.SetString(Recover2StageKey, StageProbe);
                        SetCodeOptimization(CodeOptimization.Debug, "rung 2 (2/2 continuation): Debug");
                    }
                    else if (stage == StageProbe)
                    {
                        // Final reload complete; clear state and re-probe.
                        SessionState.EraseString(Recover2StageKey);
                        SessionState.EraseString(Recover2RootKey);
                        Debug.Log("[NvimUnity] dbgrecover2: flips complete; re-probing");
                        RunStatusProbe(root);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[NvimUnity] dbgrecover2 continuation failed: " + ex.Message);
                    SessionState.EraseString(Recover2StageKey);
                    SessionState.EraseString(Recover2RootKey);
                }
            });
        }

        private static void SetCodeOptimization(CodeOptimization value, string why)
        {
            VLog("dbgrecover2: setting codeOptimization = " + value + " (" + why + ")");
            CompilationPipeline.codeOptimization = value;
        }

        // =====================================================================
        // dbgrecover3 — scripted editor restart (main-thread entry)
        // =====================================================================
        //
        // WHY (guaranteed rung): a fresh editor process always re-arms the agent (and gets a
        // fresh PID -> fresh 56000+pid%1000 port, which Start's editor-pid.txt rewrite covers).
        // This just makes the "restart the editor" fix one keypress: save scenes + assets,
        // then relaunch. RequestCloseAndRelaunchWithCurrentArguments is internal (precedent:
        // Unity's InputSystem calls it) so it's reflected; OpenProject(current) is the fallback.
        public static void Recover3_RestartEditor(string root)
        {
            try
            {
                Debug.Log("[NvimUnity] dbgrecover3: saving modified scenes + assets, then restarting the editor…");

                try { EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo(); }
                catch (Exception ex) { Debug.LogWarning("[NvimUnity] dbgrecover3: scene save failed: " + ex.Message); }

                try { AssetDatabase.SaveAssets(); }
                catch (Exception ex) { Debug.LogWarning("[NvimUnity] dbgrecover3: asset save failed: " + ex.Message); }

                var mi = typeof(EditorApplication).GetMethod(
                    "RequestCloseAndRelaunchWithCurrentArguments",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
                if (mi != null)
                {
                    Debug.Log("[NvimUnity] dbgrecover3: EditorApplication.RequestCloseAndRelaunchWithCurrentArguments()");
                    mi.Invoke(null, null);
                    return;
                }

                // Fallback: relaunch by opening the current project path.
                string projectPath = root;
                if (string.IsNullOrEmpty(projectPath))
                {
                    // Application.dataPath is <root>/Assets (main thread — we are on it here).
                    projectPath = Path.GetDirectoryName(Application.dataPath);
                }
                Debug.LogWarning("[NvimUnity] dbgrecover3: relaunch method not found; falling back to EditorApplication.OpenProject(\""
                    + projectPath + "\")");
                EditorApplication.OpenProject(projectPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[NvimUnity] dbgrecover3 failed: " + ex.Message);
            }
        }

        // =====================================================================
        // Shared helpers
        // =====================================================================

        // Resolves UnityEditor.Scripting.ManagedDebugger by reflection. The plan documents it
        // as public editor API for 2022.3, but its exact member surface isn't guaranteed
        // stable, so callers reflect into it and log a clear failure rather than hard-linking.
        private static Type ResolveManagedDebuggerType()
        {
            return Type.GetType("UnityEditor.Scripting.ManagedDebugger, UnityEditor")
                ?? Type.GetType("UnityEditor.Scripting.ManagedDebugger, UnityEditor.CoreModule")
                ?? Type.GetType("UnityEditor.Scripting.ManagedDebugger");
        }

        // Reads ManagedDebugger.isAttached. Returns false + logs if the API surface differs.
        private static bool TryGetDebuggerAttached(out bool attached)
        {
            attached = false;
            try
            {
                var t = ResolveManagedDebuggerType();
                if (t == null)
                {
                    VLog("ManagedDebugger type not found; skipping attach check");
                    return false;
                }
                var prop = t.GetProperty("isAttached",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop == null)
                {
                    VLog("ManagedDebugger.isAttached not found; skipping attach check");
                    return false;
                }
                attached = (bool)prop.GetValue(null, null);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[NvimUnity] ManagedDebugger.isAttached probe failed: " + ex.Message);
                return false;
            }
        }

        // Runs `action` on the main thread after `delaySeconds`, via an EditorApplication.update
        // poll. MUST be called from the main thread (DrainQueue / delayCall) — it reads
        // EditorApplication.timeSinceStartup and mutates the update callback list.
        private static void ScheduleMainThread(double delaySeconds, Action action)
        {
            double due = EditorApplication.timeSinceStartup + delaySeconds;
            EditorApplication.CallbackFunction cb = null;
            cb = () =>
            {
                if (EditorApplication.timeSinceStartup < due) return;
                EditorApplication.update -= cb;
                try { action(); }
                catch (Exception ex) { Debug.LogWarning("[NvimUnity] scheduled recovery task failed: " + ex.Message); }
            };
            EditorApplication.update += cb;
        }
    }
}
