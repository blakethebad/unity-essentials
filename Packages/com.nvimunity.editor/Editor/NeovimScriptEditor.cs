using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using NvimUnity.Editor;
using Unity.CodeEditor;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace NvimUnity.Editor
{

    [InitializeOnLoad]
    public class NeovimScriptEditor : IExternalCodeEditor
    {
        private const string PrefTerminalExe   = "NvimUnity.TerminalExe";
        private const string PrefLauncherTpl   = "NvimUnity.LauncherTpl";
        private const string PrefClientPipeKey = "NvimUnity.Pipe.";

        // Per-platform launch defaults. Placeholders: {nvim} {pipe} {file} {line} {column}.
    #if UNITY_EDITOR_WIN
        private const string DefaultTerminalExe = "powershell.exe";
        private const string DefaultLauncherTpl =
            "-NoLogo -NoProfile -Command \"& '{nvim}' --listen '{pipe}' '+call cursor({line},{column})' '{file}'\"";
    #elif UNITY_EDITOR_OSX
        // Drives Terminal.app through osascript, the one mac terminal that needs no setup.
        // Quoting nests three deep: Mono arguments, then AppleScript string, then shell.
        // Paths with spaces or quotes need a custom template.
        private const string DefaultTerminalExe = "/usr/bin/osascript";
        private const string DefaultLauncherTpl =
            "-e \"tell application \\\"Terminal\\\" to do script \\\"{nvim} --listen {pipe} '+call cursor({line},{column})' {file}\\\"\""
            + " -e \"tell application \\\"Terminal\\\" to activate\"";
    #else
        // Linux (untested). x-terminal-emulator is Debian's shim; change it in Preferences.
        private const string DefaultTerminalExe = "x-terminal-emulator";
        private const string DefaultLauncherTpl =
            "-e \"{nvim} --listen {pipe} '+call cursor({line},{column})' {file}\"";
    #endif

        private static readonly string[] _supportedFileNames = { "nvim.exe", "nvim" };

        //Not readonly: the Preferences dropdown swaps it live (see SwapGenerator).
        private IGenerator _projectGeneration;
        private static NeovimScriptEditor _instance;
        private static bool _warnedNoInstallation;

        //The one place a generator is chosen. Classic is the default, SDK-style is opt-in.
        internal static IGenerator CreateGenerator(string projectRoot)
        {
            switch (NvimUnityConfig.instance.GeneratorType)
            {
                case ProjectGeneratorType.SdkStyleRoslyn:
                    return new RoslynProjectGenerator(projectRoot);
                default:
                    return new ClassicProjectGenerator(projectRoot);
            }
        }

        static NeovimScriptEditor()
        {
            try
            {
                var projectRoot = Directory.GetParent(Application.dataPath).FullName;
                var generator = CreateGenerator(projectRoot);
                _instance = new NeovimScriptEditor(generator);
                CodeEditor.Register(_instance);

                // Always listen, so Neovim still works with another external editor selected.
                NeovimSyncServer.Start(projectRoot, generator);

                if (IsNeovimInstallation(CodeEditor.CurrentEditorInstallation))
                    _instance.CreateIfDoesntExist();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        public NeovimScriptEditor(IGenerator projectGeneration)
        {
            _projectGeneration = projectGeneration;
        }

        //Rebuild the generator after a Preferences switch, keeping the current flags. The sync
        //server has to be handed the new instance (Start will not swap it), then files are
        //regenerated so they match the new selection right away.
        private void SwapGenerator()
        {
            var root = _projectGeneration.ProjectDirectory;
            var flags = _projectGeneration.ProjectGenerationFlag;
            _projectGeneration = CreateGenerator(root);
            _projectGeneration.ProjectGenerationFlag = flags;
            NeovimSyncServer.SetGenerator(_projectGeneration);
            _projectGeneration.SyncProject();
        }

        public CodeEditor.Installation[] Installations
        {
            get
            {
                var cached = NeovimScriptEditorConfig.instance.installations;
                if (cached != null && cached.Length > 0)
                    return cached.Select(i => new CodeEditor.Installation { Name = i.Presentation, Path = i.Path }).ToArray();

                // Known install locations. Needed on mac: Unity launched from the Dock gets a
                // stripped PATH with no Homebrew in it, so scanning PATH alone finds nothing.
                var candidates = new List<string>();
    #if UNITY_EDITOR_WIN
                const string exeName = "nvim.exe";
                var pf  = Environment.GetEnvironmentVariable("ProgramFiles");
                var lad = Environment.GetEnvironmentVariable("LOCALAPPDATA");
                var up  = Environment.GetEnvironmentVariable("USERPROFILE");

                if (!string.IsNullOrEmpty(pf))  candidates.Add(Path.Combine(pf,  "Neovim", "bin", "nvim.exe"));
                if (!string.IsNullOrEmpty(lad)) candidates.Add(Path.Combine(lad, "Programs", "Neovim", "bin", "nvim.exe"));
                if (!string.IsNullOrEmpty(up))  candidates.Add(Path.Combine(up,  "scoop", "apps", "neovim", "current", "bin", "nvim.exe"));
    #else
                const string exeName = "nvim";
                candidates.Add("/opt/homebrew/bin/nvim"); // Homebrew, Apple Silicon
                candidates.Add("/usr/local/bin/nvim");    // Homebrew, Intel / manual installs
                candidates.Add("/usr/bin/nvim");          // distro package (linux)
    #endif

                var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (var dir in pathEnv.Split(Path.PathSeparator))
                {
                    if (string.IsNullOrEmpty(dir)) continue;
                    var cand = Path.Combine(dir, exeName);
                    if (!candidates.Contains(cand)) candidates.Add(cand);
                }

                var found = new List<NeovimInfo>();
                foreach (var c in candidates)
                {
                    if (!File.Exists(c)) continue;
                    if (found.Any(f => string.Equals(f.Path, c, StringComparison.OrdinalIgnoreCase))) continue;
                    // BuildNumber left empty; filling it would cost an nvim --version call per candidate.
                    found.Add(new NeovimInfo { Path = c, Presentation = "Neovim", BuildNumber = "" });
                }

                NeovimScriptEditorConfig.instance.installations = found.ToArray();
                return found.Select(i => new CodeEditor.Installation { Name = i.Presentation, Path = i.Path }).ToArray();
            }
        }

        public bool TryGetInstallationForPath(string editorPath, out CodeEditor.Installation installation)
        {
            installation = default;
            if (string.IsNullOrEmpty(editorPath)) return false;

            var filename = Path.GetFileName(editorPath).ToLowerInvariant();
            if (!_supportedFileNames.Contains(filename)) return false;
            if (!File.Exists(editorPath)) return false;

            var match = Installations.FirstOrDefault(i => string.Equals(i.Path, editorPath, StringComparison.OrdinalIgnoreCase));
            installation = match.Path != null
                ? match
                : new CodeEditor.Installation { Name = "Neovim", Path = editorPath };
            return true;
        }

        public void Initialize(string editorInstallationPath)
        {
            NeovimSyncServer.Start(_projectGeneration.ProjectDirectory, _projectGeneration);
            CreateIfDoesntExist();
        }

        public void OnGUI()
        {
            var term = EditorPrefs.GetString(PrefTerminalExe, DefaultTerminalExe);
            var tpl  = EditorPrefs.GetString(PrefLauncherTpl, DefaultLauncherTpl);

            EditorGUI.BeginChangeCheck();
            term = EditorGUILayout.TextField(new GUIContent("Terminal Executable"), term);
            tpl  = EditorGUILayout.TextField(
                new GUIContent("Launcher Template", "Placeholders: {nvim} {pipe} {file} {line} {column}"
    #if UNITY_EDITOR_OSX
                    + "\n\nmac default drives Terminal.app via osascript."
                    + "\niTerm2: Terminal Executable /usr/bin/open, template: -na iTerm --args {nvim} --listen {pipe} {file}"
                    + "\nkitty: Terminal Executable /opt/homebrew/bin/kitty, template: {nvim} --listen {pipe} {file}"
                    + "\nghostty: Terminal Executable /usr/bin/open, template: -na Ghostty --args -e {nvim} --listen {pipe} \"+call cursor({line},{column})\" \"{file}\""
    #endif
                ),
                tpl);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetString(PrefTerminalExe, term);
                EditorPrefs.SetString(PrefLauncherTpl, tpl);
            }
            if (GUILayout.Button("Reset Template", GUILayout.Width(140)))
                EditorPrefs.SetString(PrefLauncherTpl, DefaultLauncherTpl);

            EditorGUILayout.Space();
            var cfg = NvimUnityConfig.instance;
            var newType = (ProjectGeneratorType)EditorGUILayout.EnumPopup(
                new GUIContent("Project Generation",
                    "Classic = legacy-style csproj (the pre-SDK MSBuild shape). SDK-style (Roslyn) = modern <Project Sdk=...> csproj."),
                cfg.GeneratorType);
            if (newType != cfg.GeneratorType)
            {
                cfg.GeneratorType = newType; // persists via Save()
                SwapGenerator();
            }
            if (cfg.GeneratorType == ProjectGeneratorType.SdkStyleRoslyn)
            {
                EditorGUI.indentLevel++;
                var tf = EditorGUILayout.TextField(
                    new GUIContent("Target Framework",
                        "Empty/auto = per-assembly from its API compatibility level (net471 / netstandard2.1)."),
                    cfg.TargetFramework);
                if (tf != cfg.TargetFramework) cfg.TargetFramework = tf;
                var lv = EditorGUILayout.TextField(
                    new GUIContent("Lang Version",
                        "Empty/latest/auto = Unity's own pinned compiler language version."),
                    cfg.LangVersion);
                if (lv != cfg.LangVersion) cfg.LangVersion = lv;
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Generate .csproj files for:");
            EditorGUI.indentLevel++;
            ToggleFlag(ProjectGenerationFlag.Embedded,     "Embedded packages");
            ToggleFlag(ProjectGenerationFlag.Local,        "Local packages");
            ToggleFlag(ProjectGenerationFlag.Registry,     "Registry packages");
            ToggleFlag(ProjectGenerationFlag.Git,          "Git packages");
            ToggleFlag(ProjectGenerationFlag.BuiltIn,      "Built-in packages");
            ToggleFlag(ProjectGenerationFlag.LocalTarBall, "Local tarball");
            ToggleFlag(ProjectGenerationFlag.Unknown,      "Packages from unknown sources");
            EditorGUI.indentLevel--;

            EditorGUILayout.Space();
            if (GUILayout.Button("Regenerate project files", GUILayout.Width(220)))
                _projectGeneration.SyncProject();
            if (GUILayout.Button("Refresh Neovim installations", GUILayout.Width(220)))
                NeovimScriptEditorConfig.instance.installations = null;
        }

        private void ToggleFlag(ProjectGenerationFlag flag, string label)
        {
            var prev = _projectGeneration.ProjectGenerationFlag.HasFlag(flag);
            var next = EditorGUILayout.Toggle(label, prev);
            if (next == prev) return;
            _projectGeneration.ProjectGenerationFlag = next
                ? _projectGeneration.ProjectGenerationFlag | flag
                : _projectGeneration.ProjectGenerationFlag & ~flag;
        }

        public bool OpenProject(string filePath = "", int line = -1, int column = -1)
        {
            var nvimExe = CodeEditor.CurrentEditorInstallation;
            if (string.IsNullOrEmpty(nvimExe) || !File.Exists(nvimExe))
            {
                if (!_warnedNoInstallation)
                {
                    _warnedNoInstallation = true;
                    Debug.LogError("[NvimUnity] No Neovim installation configured. Set one in Edit > Preferences > External Tools.");
                }
                return false;
            }

            if (!string.IsNullOrEmpty(filePath))
            {
                if (!_projectGeneration.HasValidExtension(filePath)) return false;
                if (!File.Exists(filePath)) return false;
            }

            if (line   <= 0) line   = 1;
            if (column <  0) column = 0;
            var cursorCol = column == 0 ? 1 : column;

            var projectRoot = _projectGeneration.ProjectDirectory;
            var fileForArg  = string.IsNullOrEmpty(filePath) ? projectRoot : filePath;

            var clientPipe = EditorPrefs.GetString(
                PrefClientPipeKey + projectRoot,
                NeovimSyncServer.DefaultClientPipeNameFor(projectRoot));

            // 0) Preferred: ask the running nvim (address from nvim-server.txt) to open the file
            //    itself. Works even for an nvim the user started by hand.
            var serverAddr  = ReadServerAddress(projectRoot);
            var serverAlive = !string.IsNullOrEmpty(serverAddr) && IsServerReachable(serverAddr);
            if (serverAlive)
            {
                if (TryRemoteExprOpen(nvimExe, serverAddr, fileForArg, line, cursorCol))
                    return true;

                // nvim is alive but busy. Never spawn a second one for the same project: report
                // handled and let the user retry once it is idle.
                return true;
            }

            // 1) Fallback: the per-project pipe Unity told nvim to --listen on, driven by
            //    --remote-send.
            if (!serverAlive && IsServerReachable(clientPipe))
            {
                var keys = new StringBuilder();
                keys.Append(@"<C-\><C-N>:e ");
                keys.Append(fileForArg.Replace(" ", @"\ "));
                keys.Append("<CR>:call cursor(").Append(line).Append(",").Append(cursorCol).Append(")<CR>");

                var psi = new ProcessStartInfo
                {
                    FileName               = nvimExe,
                    Arguments              = "--server \"" + clientPipe + "\" --remote-send \"" + keys + "\"",
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                };
                try
                {
                    using (var p = Process.Start(psi))
                    {
                        if (p != null && p.WaitForExit(2000) && p.ExitCode == 0)
                            return true;
                    }
                }
                catch (Exception)
                {
                }
            }

            // 2) Spawn a fresh terminal + nvim listening on the per-project client pipe.
            var newPipe = NeovimSyncServer.DefaultClientPipeNameFor(projectRoot);
            EditorPrefs.SetString(PrefClientPipeKey + projectRoot, newPipe);

            var terminalExe = EditorPrefs.GetString(PrefTerminalExe, DefaultTerminalExe);
            var template    = EditorPrefs.GetString(PrefLauncherTpl, DefaultLauncherTpl);
            var cmd = template
                .Replace("{nvim}",   nvimExe)
                .Replace("{pipe}",   newPipe)
                .Replace("{file}",   fileForArg)
                .Replace("{line}",   line.ToString())
                .Replace("{column}", cursorCol.ToString());

            try
            {
                var spawnPsi = new ProcessStartInfo
                {
                    FileName         = terminalExe,
                    Arguments        = cmd,
                    CreateNoWindow   = false,
                    WorkingDirectory = projectRoot,
                };
    #if UNITY_EDITOR_WIN
                spawnPsi.UseShellExecute = true;  // today's verified Windows behavior
    #else
                // Mono on Unix has no real ShellExecute; run the launcher binary directly.
                spawnPsi.UseShellExecute = false;
    #endif
                Process.Start(spawnPsi);
                return true;
            }
            catch (Win32Exception ex)
            {
                Debug.LogError("[NvimUnity] Could not launch terminal '" + terminalExe + "': " + ex.Message +
                    ". Configure Terminal Executable / Launcher Template in Edit > Preferences > External Tools.");
                return false;
            }
        }

        public void SyncAll()
        {
            _projectGeneration.SyncProject();
        }

        public void SyncIfNeeded(string[] addedFiles, string[] deletedFiles, string[] movedFiles, string[] movedFromFiles, string[] importedFiles)
        {
            _projectGeneration.SyncIfNeeded(addedFiles.Union(deletedFiles).Union(movedFiles).Union(movedFromFiles), importedFiles);
        }

        private void CreateIfDoesntExist()
        {
            var slnPath = Path.Combine(_projectGeneration.ProjectDirectory, _projectGeneration.ProjectName + ".sln");
            if (!File.Exists(slnPath))
                _projectGeneration.SyncProject();
        }

        private static bool IsNeovimInstallation(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var name = Path.GetFileName(path).ToLowerInvariant();
            return _supportedFileNames.Contains(name);
        }

        // Reads the address the running nvim wrote for this project. One line, may be stale.
        private static string ReadServerAddress(string projectRoot)
        {
            try
            {
                var path = Path.Combine(projectRoot, "Library", "NvimUnity", "nvim-server.txt");
                if (!File.Exists(path)) return null;
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (!string.IsNullOrEmpty(line)) return line;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        // Runs a throwaway nvim client that tells the running nvim to open the file at line:col
        // and raise its terminal. True when that client exits cleanly within 2s.
        private static bool TryRemoteExprOpen(string nvimExe, string serverAddr, string fileForArg, int line, int column)
        {
            var expr = BuildRemoteExpr(fileForArg, line, column);

            var args = new StringBuilder();
            AppendArgument(args, "--server");
            AppendArgument(args, serverAddr);
            AppendArgument(args, "--remote-expr");
            AppendArgument(args, expr);

            var psi = new ProcessStartInfo
            {
                FileName               = nvimExe,
                Arguments              = args.ToString(),
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            };
            try
            {
                using (var p = Process.Start(psi))
                {
                    // Do not read stdout/stderr (return capture is flaky on Windows); trust exit code.
                    if (p != null && p.WaitForExit(2000) && p.ExitCode == 0)
                        return true;
                }
            }
            catch (Exception)
            {
            }
            return false;
        }

        // v:lua.require'unity.remote'.open('<file>', <line>, <col>) — forward-slash the path, double any '.
        private static string BuildRemoteExpr(string fileForArg, int line, int column)
        {
            var f = fileForArg.Replace('\\', '/').Replace("'", "''");
            return "v:lua.require'unity.remote'.open('" + f + "', " + line + ", " + column + ")";
        }

        // Windows and Mono parse ProcessStartInfo.Arguments by different rules, so each platform
        // needs its own quoting.
        private static void AppendArgument(StringBuilder sb, string arg)
        {
    #if UNITY_EDITOR_WIN
            AppendWindowsArgument(sb, arg);
    #else
            AppendUnixArgument(sb, arg);
    #endif
        }

        // Mono on Unix uses shell-like rules: quote the argument and escape " and \ inside it.
        private static void AppendUnixArgument(StringBuilder sb, string arg)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append('"');
            foreach (var ch in arg ?? string.Empty)
            {
                if (ch == '"' || ch == '\\') sb.Append('\\');
                sb.Append(ch);
            }
            sb.Append('"');
        }

        // Windows rules: quote when the argument holds whitespace or ", and double any run of
        // backslashes that butts against a quote. ArgumentList does not exist on .NET 4.7.1.
        private static void AppendWindowsArgument(StringBuilder sb, string arg)
        {
            if (sb.Length > 0) sb.Append(' ');

            if (!string.IsNullOrEmpty(arg) && arg.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0)
            {
                sb.Append(arg);
                return;
            }

            sb.Append('"');
            for (int i = 0; i < arg.Length; i++)
            {
                int backslashes = 0;
                while (i < arg.Length && arg[i] == '\\')
                {
                    i++;
                    backslashes++;
                }

                if (i == arg.Length)
                {
                    sb.Append('\\', backslashes * 2);
                    break;
                }
                else if (arg[i] == '"')
                {
                    sb.Append('\\', backslashes * 2 + 1);
                    sb.Append('"');
                }
                else
                {
                    sb.Append('\\', backslashes);
                    sb.Append(arg[i]);
                }
            }
            sb.Append('"');
        }

        // The address says which transport it is: tcp:host:port, a /unix/socket/path, or a
        // Windows pipe. Never hand a unix path to the pipe probe, it always answers false.
        private static bool IsServerReachable(string addr)
        {
            if (string.IsNullOrEmpty(addr)) return false;
            if (addr.StartsWith("tcp:", StringComparison.Ordinal)) return IsTcpReachable(addr);
            if (addr.StartsWith("/", StringComparison.Ordinal))    return IsUnixSocketReachable(addr);
            return IsPipeReachable(addr);
        }

        private static bool IsTcpReachable(string addr)
        {
            var rest = addr.Substring("tcp:".Length);
            var idx  = rest.LastIndexOf(':');
            int port;
            if (idx <= 0 || !int.TryParse(rest.Substring(idx + 1), out port)) return false;
            var host = rest.Substring(0, idx);
            try
            {
                using (var c = new TcpClient())
                {
                    var ar = c.BeginConnect(host, port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(200)) return false;
                    c.EndConnect(ar);
                    return c.Connected;
                }
            }
            catch
            {
                return false;
            }
        }

        // Hand-rolled AF_UNIX endpoint, because Mono.Unix is not referenced here. Only a real
        // connect proves liveness: socket files linger after nvim crashes.
        private sealed class UnixEndPoint : EndPoint
        {
            private readonly string _path;
            public UnixEndPoint(string path) { _path = path; }
            public override AddressFamily AddressFamily { get { return AddressFamily.Unix; } }
            public override SocketAddress Serialize()
            {
                var bytes = Encoding.UTF8.GetBytes(_path);
                var sa = new SocketAddress(AddressFamily.Unix, 2 + bytes.Length + 1);
                for (int i = 0; i < bytes.Length; i++) sa[2 + i] = bytes[i];
                sa[2 + bytes.Length] = 0;
                return sa;
            }
        }

        private static bool IsUnixSocketReachable(string path)
        {
            try
            {
                using (var s = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified))
                {
                    var ar = s.BeginConnect(new UnixEndPoint(path), null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(200)) return false;
                    s.EndConnect(ar);
                    return s.Connected;
                }
            }
            catch
            {
                return false;
            }
        }

        // Windows probe. Connect(timeout) throws when nothing is listening on the pipe.
        private static bool IsPipeReachable(string pipeName)
        {
            const string prefix = @"\\.\pipe\";
            var local = pipeName.StartsWith(prefix) ? pipeName.Substring(prefix.Length) : pipeName;
            try
            {
                using (var c = new NamedPipeClientStream(".", local, PipeDirection.InOut))
                {
                    c.Connect(200);
                    return c.IsConnected;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
