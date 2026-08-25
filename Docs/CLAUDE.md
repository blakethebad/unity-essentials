# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Purpose

This is a **library project**, not a game. It collects the essential, reusable tools, packages, and systems (e.g. singletons, object pooling, event systems, save systems, extension methods, editor utilities) that will be imported into other Unity projects. Everything added here must be written to be portable:

- Keep each system self-contained in its own folder with no dependencies on scene setup or other systems, unless the dependency is explicit and documented.
- Do not reference project-specific assets (scenes, prefabs from the template, tags/layers) from reusable code.
- Give reusable code assembly definitions (`.asmdef`) so it can be exported/imported cleanly and compiles independently of `Assembly-CSharp`. Editor-only code goes in an `Editor` folder with an editor-only asmdef.
- `Assets/Scenes/SampleScene.unity` and any test scenes exist only for trying systems out — reusable code must never depend on them.

## Environment

- **Unity version:** 6000.5.6f1 (Unity 6), installed at `C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe`
- **Render pipeline:** URP (Universal 3D template) — separate PC and Mobile renderer/pipeline assets live in `Assets/Settings/`
- **Input:** New Input System (`com.unity.inputsystem`) is installed, but there is no shared actions asset — this is a library project, not a game; systems needing input define their own.
- Reusable systems live under `Assets/Packages/` (not to be confused with the top-level `Packages/` folder managed by Unity Package Manager).

## Commands

Unity work happens primarily in the Editor; there is no build/lint step to run from the CLI. C# compilation is verified by Unity generating/compiling the solution — check `Logs/` or the Editor console for errors.

Run tests headlessly with the Unity Test Framework (`com.unity.test-framework` is installed):

```powershell
# EditMode tests
& "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe" -batchmode -projectPath "C:\Users\user\Documents\UnityProjects\Personal\unity-essentials" -runTests -testPlatform EditMode -testResults "Logs\editmode-results.xml" -logFile "Logs\test-run.log"

# PlayMode tests: use -testPlatform PlayMode
# Single test/class: add -testFilter "Namespace.ClassName" (or "Namespace.ClassName.MethodName")
```

Note: batchmode fails if the project is already open in the Editor. Test results are NUnit XML in the path given to `-testResults`.

## Working Conventions

- When editing assets, never touch files under `Library/`, `Temp/`, `Logs/`, `obj/`, or `UserSettings/` — these are generated. Source of truth is `Assets/`, `Packages/`, and `ProjectSettings/`.
- Every file/folder under `Assets/` has a paired `.meta` file containing its GUID. When creating files via CLI, let Unity generate the `.meta`; when moving/renaming/deleting, move/rename/delete the `.meta` alongside it or references will break.
- Prefer `[SerializeField] private` fields over public fields for inspector exposure, and namespaces for all reusable code so imports into other projects don't collide.
- New packages are added by editing `Packages/manifest.json` (Unity resolves them on next Editor focus).
