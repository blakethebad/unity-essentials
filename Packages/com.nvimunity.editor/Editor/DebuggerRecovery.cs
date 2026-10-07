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

namespace NvimUnity.Editor
{
    // Unity's Mono debugger agent can wedge: after a messy client detach it stops answering the
    // handshake, so later attaches bind no breakpoints. dbgstatus classifies the agent and
    // dbgrecover1/2/3 fix it; both report to Library/NvimUnity/dbg-status.txt.
    internal static class DebuggerRecovery
    {
        private const string StatusFileName = "dbg-status.txt";

        // SessionState survives domain reloads and clears on quit, which is exactly the
        // lifetime recover2's flip sequence needs.
        private const string Recover2StageKey = "NvimUnity.DbgRecover2.Stage";
        private const string Recover2RootKey  = "NvimUnity.DbgRecover2.Root";
        private const string StageSetDebug = "SetDebug"; // after this reload: flip to Debug
        private const string StageProbe    = "Probe";    // after this reload: re-probe + finish

        // Is the agent armed, wedged, attached, or not listening? Check isAttached first: the
        // agent takes one client, so an attached debugger looks just like a wedge on the wire.
        // The socket probe blocks for seconds, so it runs off the main thread.
        public static void RunStatusProbe(string root)
        {
            try
            {
                int port = NeovimSyncServer.DebuggerPortForCurrentProcess();

                // Attach check first: busy is not the same as wedged.
                bool attached;
                if (TryGetDebuggerAttached(out attached) && attached)
                {
                    WriteStatus(root, "attached", port);
                    return;
                }

                // Nothing attached, so probing is safe.
                Task.Run(() =>
                {
                    string status;
                    try { status = ProbeAgentSocket(port); }
                    catch (Exception)
                    {
                        status = "error";
                    }
                    WriteStatus(root, status, port);
                });
            }
            catch (Exception)
            {
            }
        }

        // Background thread. No connection means "no-listener", a connection with no handshake
        // means "wedged", a full handshake means "armed" — and finishing the handshake is a
        // clean detach, so a successful probe leaves the agent ready for the next attach.
        private static string ProbeAgentSocket(int port)
        {
            TcpClient client = null;
            try
            {
                client = new TcpClient();
                // .NET 4.7.1: no connect-timeout overload; BeginConnect + WaitOne is the idiom.
                var ar = client.BeginConnect(IPAddress.Loopback, port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(1000))
                    return "no-listener";
                try
                {
                    client.EndConnect(ar);
                }
                catch (SocketException)
                {
                    // Connection refused (nothing listening on the port).
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
                    return "wedged";

                // Echo the handshake, then VM_DISPOSE: the clean detach that re-arms the agent.
                stream.Write(handshake, 0, handshake.Length);
                stream.Flush();
                byte[] dispose = BuildVmDispose();
                stream.Write(dispose, 0, dispose.Length);
                stream.Flush();
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

        // VM_DISPOSE packet: an 11-byte big-endian header, no data.
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

        // Writes one line of status JSON to Library/NvimUnity/dbg-status.txt, which nvim reads.
        // Safe from the probe thread: file writes are.
        private static void WriteStatus(string root, string status, int port)
        {
            string iso = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            string json = "{\"status\":\"" + status + "\",\"checked_at\":\"" + iso + "\",\"port\":" + port + "}";
            try
            {
                var dir = Path.Combine(root ?? string.Empty, "Library", "NvimUnity");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, StatusFileName), json);
            }
            catch (Exception)
            {
            }
        }

        // Cheapest fix: Disconnect() should stop the debugger thread and let the agent
        // re-initialize, with no recompile. Unproven against a real wedge, so it re-probes
        // afterwards to show whether it worked.
        public static void Recover1_Disconnect(string root)
        {
            try
            {
                var t = ResolveManagedDebuggerType();
                if (t != null)
                {
                    var mi = t.GetMethod("Disconnect",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                        null, Type.EmptyTypes, null);
                    if (mi != null)
                        mi.Invoke(null, null);
                }

                // Re-probe ~1s later so the result reflects the post-Disconnect state.
                ScheduleMainThread(1.0, () => RunStatusProbe(root));
            }
            catch (Exception)
            {
            }
        }

        // Middle fix: flipping codeOptimization Debug to Release to Debug is the same switch as
        // the status bar Debug button and forces the agent to rebind. It costs two recompiles,
        // so the stage is kept in SessionState and Recover2Continuation resumes after each one.
        public static void Recover2_CodeOptimizationFlip(string root)
        {
            try
            {
                var current = CompilationPipeline.codeOptimization;
                SessionState.SetString(Recover2RootKey, root ?? string.Empty);

                if (current == CodeOptimization.Release)
                {
                    SessionState.SetString(Recover2StageKey, StageProbe);
                    SetCodeOptimization(CodeOptimization.Debug);
                }
                else
                {
                    SessionState.SetString(Recover2StageKey, StageSetDebug);
                    SetCodeOptimization(CodeOptimization.Release);
                }
            }
            catch (Exception)
            {
            }
        }

        // Runs on every domain load and does nothing unless a flip is in progress. Waits with an
        // update poll, not delayCall: a delayCall here was seen to get dropped after a reload.
        [InitializeOnLoadMethod]
        private static void Recover2Continuation()
        {
            string stage = SessionState.GetString(Recover2StageKey, string.Empty);
            if (string.IsNullOrEmpty(stage)) return;

            string root = SessionState.GetString(Recover2RootKey, string.Empty);

            ScheduleMainThread(1.0, () =>
            {
                try
                {
                    if (stage == StageSetDebug)
                    {
                        // First reload complete (now in Release); flip back to Debug.
                        SessionState.SetString(Recover2StageKey, StageProbe);
                        SetCodeOptimization(CodeOptimization.Debug);
                    }
                    else if (stage == StageProbe)
                    {
                        // Final reload complete; clear state and re-probe.
                        SessionState.EraseString(Recover2StageKey);
                        SessionState.EraseString(Recover2RootKey);
                        RunStatusProbe(root);
                    }
                }
                catch (Exception)
                {
                    SessionState.EraseString(Recover2StageKey);
                    SessionState.EraseString(Recover2RootKey);
                }
            });
        }

        private static void SetCodeOptimization(CodeOptimization value)
        {
            CompilationPipeline.codeOptimization = value;
        }

        // Last resort, but always works: a fresh editor re-arms the agent. Saves scenes and
        // assets, then relaunches through an internal editor method, falling back to reopening
        // the project.
        public static void Recover3_RestartEditor(string root)
        {
            try
            {
                try { EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo(); }
                catch (Exception) { }

                try { AssetDatabase.SaveAssets(); }
                catch (Exception) { }

                var mi = typeof(EditorApplication).GetMethod(
                    "RequestCloseAndRelaunchWithCurrentArguments",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
                if (mi != null)
                {
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
                EditorApplication.OpenProject(projectPath);
            }
            catch (Exception)
            {
            }
        }

        // Found by reflection because its member surface is not stable across Unity versions.
        private static Type ResolveManagedDebuggerType()
        {
            return Type.GetType("UnityEditor.Scripting.ManagedDebugger, UnityEditor")
                ?? Type.GetType("UnityEditor.Scripting.ManagedDebugger, UnityEditor.CoreModule")
                ?? Type.GetType("UnityEditor.Scripting.ManagedDebugger");
        }

        // Reads ManagedDebugger.isAttached. Returns false if the API surface differs.
        private static bool TryGetDebuggerAttached(out bool attached)
        {
            attached = false;
            try
            {
                var t = ResolveManagedDebuggerType();
                if (t == null) return false;

                var prop = t.GetProperty("isAttached",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop == null) return false;

                attached = (bool)prop.GetValue(null, null);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Runs `action` after a delay by polling EditorApplication.update. Main thread only.
        private static void ScheduleMainThread(double delaySeconds, Action action)
        {
            double due = EditorApplication.timeSinceStartup + delaySeconds;
            EditorApplication.CallbackFunction cb = null;
            cb = () =>
            {
                if (EditorApplication.timeSinceStartup < due) return;
                EditorApplication.update -= cb;
                try { action(); }
                catch (Exception) { }
            };
            EditorApplication.update += cb;
        }
    }
}
