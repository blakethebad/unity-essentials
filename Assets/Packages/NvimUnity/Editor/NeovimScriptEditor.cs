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
using NvimUnity;
using NvimUnity.Editor;
using Unity.CodeEditor;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

[InitializeOnLoad]
public class NeovimScriptEditor : IExternalCodeEditor
{
    private const string PrefTerminalExe   = "NvimUnity.TerminalExe";
    private const string PrefLauncherTpl   = "NvimUnity.LauncherTpl";
    private const string PrefClientPipeKey = "NvimUnity.Pipe.";

    // Per-platform spawn-fallback defaults (MACOS_PLAN U4). The mechanism (EditorPrefs +
    // {nvim}/{pipe}/{file}/{line}/{column} placeholders) is shared; only the defaults branch.
#if UNITY_EDITOR_WIN
    private const string DefaultTerminalExe = "powershell.exe";
    private const string DefaultLauncherTpl =
        "-NoLogo -NoProfile -Command \"& '{nvim}' --listen '{pipe}' '+call cursor({line},{column})' '{file}'\"";
#elif UNITY_EDITOR_OSX
    // osascript into Terminal.app — the only mac terminal reachable without extra setup
    // (`open -na iTerm --args …` has its own quirks; see the Launcher Template tooltip for
    // iTerm2/kitty examples). Mono on Unix parses Arguments with shell-like quoting rules,
    // so the template nests: Mono-level "…\"…\"…" → AppleScript string → shell command.
    // Paths containing spaces/quotes are not survivable in this default; configure a
    // custom template if needed.
    private const string DefaultTerminalExe = "/usr/bin/osascript";
    private const string DefaultLauncherTpl =
        "-e \"tell application \\\"Terminal\\\" to do script \\\"{nvim} --listen {pipe} '+call cursor({line},{column})' {file}\\\"\""
        + " -e \"tell application \\\"Terminal\\\" to activate\"";
#else
    // Linux (unverified — MACOS_PLAN §2.3): x-terminal-emulator is the Debian alternatives
    // shim; adjust in Preferences on other distros.
    private const string DefaultTerminalExe = "x-terminal-emulator";
    private const string DefaultLauncherTpl =
        "-e \"{nvim} --listen {pipe} '+call cursor({line},{column})' {file}\"";
#endif

    private static readonly string[] _supportedFileNames = { "nvim.exe", "nvim" };

    //Not readonly: the Preferences "Project Generation" dropdown swaps it live
    //(SwapGenerator below).
    private IGenerator _projectGeneration;
    private static NeovimScriptEditor _instance;
    private static bool _warnedNoInstallation;

    //Factory over NvimUnityConfig.GeneratorType — the ONE place a generator is
    //chosen. Classic stays the default; SDK-style (Roslyn) is opt-in per project.
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

            // Listener runs regardless of which editor is selected so the user can use Neovim
            // for editing while leaving the External Script Editor pointed elsewhere.
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

    //Rebuild the generator after a Preferences type switch: keep the session's
    //generation flags, hand the new instance to the sync server (Start can NOT
    //swap it — it early-returns while the listener runs), regenerate on disk so
    //the files immediately match the selection.
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

            // Per-platform candidates (MACOS_PLAN U6). The hardcoded lists are load-bearing
            // on mac: a Finder/Dock-launched Unity.app gets the launchd-restricted PATH
            // (/usr/bin:/bin:/usr/sbin:/sbin — no Homebrew), so the PATH scan finds nothing.
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
                // BuildNumber kept empty for v1 — would require an nvim --version subprocess per candidate.
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
                "Classic = legacy-style csproj (VS/Rider shape). SDK-style (Roslyn) = modern <Project Sdk=...> csproj."),
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
                Debug.LogWarning("[NvimUnity] No Neovim installation configured. Set one in Edit > Preferences > External Tools.");
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

        // 0) Preferred: reverse channel via nvim-server.txt + --remote-expr into a first-party
        //    lua entry point. Works for any nvim (even one started manually), is mode-safe, and
        //    lets nvim bring its own terminal to the foreground.
        var serverAddr  = ReadServerAddress(projectRoot);
        var serverAlive = !string.IsNullOrEmpty(serverAddr) && IsServerReachable(serverAddr);
        if (serverAlive)
        {
            if (TryRemoteExprOpen(nvimExe, serverAddr, fileForArg, line, cursorCol))
                return true;

            // A live nvim is advertised but couldn't service --remote-expr right now (busy on a
            // blocking/synchronous op, or the expr errored). Do NOT fall through to spawn a second
            // nvim on a project that already has one open. Report handled; the user can retry the
            // double-click once nvim is idle.
            Debug.LogWarning("[NvimUnity] the advertised nvim did not answer --remote-expr (busy?); " +
                "not spawning a duplicate instance. Retry once nvim is idle.");
            return true;
        }

        // 1) Fallback (only when no live advertised server): legacy per-project client pipe that
        //    Unity itself told nvim to --listen on, driven by --remote-send.
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
            catch (Exception e)
            {
                Debug.LogWarning("[NvimUnity] remote-send failed, falling back to spawn: " + e.Message);
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
            // Mono on Unix has no real ShellExecute; run the launcher binary directly so
            // the Arguments parsing (Unix shell-like rules) is deterministic.
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
        if (NeovimSyncServer.VerboseLog) Debug.Log("[NvimUnity] SyncAll -> SyncProject");
        _projectGeneration.SyncProject();
    }

    public void SyncIfNeeded(string[] addedFiles, string[] deletedFiles, string[] movedFiles, string[] movedFromFiles, string[] importedFiles)
    {
        if (NeovimSyncServer.VerboseLog)
        {
            Debug.Log("[NvimUnity] SyncIfNeeded called: added=" + addedFiles.Length +
                " deleted=" + deletedFiles.Length + " moved=" + movedFiles.Length +
                " movedFrom=" + movedFromFiles.Length + " imported=" + importedFiles.Length +
                "\n  addedFiles: " + string.Join(", ", addedFiles) +
                "\n  importedFiles: " + string.Join(", ", importedFiles));
        }
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

    // Reads the nvim server address that nvim's unity.remote module advertised for this project.
    // One line, verbatim v:servername (e.g. \\.\pipe\nvim.21536.0). Stale-tolerant.
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

    // Launches a throwaway `nvim --server <addr> --remote-expr <EXPR>` client to open the file at
    // line:col in the already-running nvim. EXPR calls a first-party lua entry point which also
    // brings nvim's terminal to the foreground. Returns true on a clean exit-0 within 2s.
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
        catch (Exception e)
        {
            Debug.LogWarning("[NvimUnity] remote-expr open failed, falling back: " + e.Message);
        }
        return false;
    }

    // v:lua.require'unity.remote'.open('<file>', <line>, <col>) — forward-slash the path, double any '.
    private static string BuildRemoteExpr(string fileForArg, int line, int column)
    {
        var f = fileForArg.Replace('\\', '/').Replace("'", "''");
        return "v:lua.require'unity.remote'.open('" + f + "', " + line + ", " + column + ")";
    }

    // Platform front door for argument quoting (MACOS_PLAN U5): ProcessStartInfo.Arguments
    // is parsed with CommandLineToArgvW rules on Windows but with Unix shell-like rules by
    // Mono on mac/linux — one quoting scheme cannot serve both. Used on the PREFERRED
    // remote-expr path, so it must be right per platform.
    private static void AppendArgument(StringBuilder sb, string arg)
    {
#if UNITY_EDITOR_WIN
        AppendWindowsArgument(sb, arg);
#else
        AppendUnixArgument(sb, arg);
#endif
    }

    // Mono on Unix parses ProcessStartInfo.Arguments with shell-like rules
    // (g_shell_parse_argv lineage): double-quote the argument and backslash-escape
    // embedded `"` and `\`.
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

    // Appends a single argument to a Windows command line using the CommandLineToArgvW quoting
    // rules (quote if it contains whitespace or ", double the run of backslashes preceding a "
    // or the closing quote). Used instead of ProcessStartInfo.ArgumentList, which is unavailable
    // in the .NET Framework 4.7.1 API profile Unity targets.
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

    // Transport-aware reachability probe (MACOS_PLAN U2). The address is self-describing:
    // `tcp:host:port`, a `/…` unix-socket path (mac nvim's v:servername / client --listen
    // socket), or a Windows pipe (`\\.\pipe\…` or bare name). Mono's managed pipe stack
    // must NOT be pointed at a unix path — it maps it to $TMPDIR/CoreFxPipe_<name>
    // nonsense and always answers false, which silently killed the preferred reverse
    // channel on mac.
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

    // Minimal AF_UNIX endpoint (sockaddr_un serialization) — Mono.Unix isn't referenced by
    // default in Assets code, and File.Exists is NOT a liveness probe here: unix socket
    // files LINGER after a crashed nvim (unlike Windows pipes), so only a real connect
    // distinguishes live from stale. A false here correctly routes to the spawn fallback.
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

    // Quick reachability probe (Windows). NamedPipeClientStream.Connect(timeout) throws when no server is listening.
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
