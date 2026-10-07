# NvimUnity

Neovim as Unity's external script editor. Registers Neovim with Unity's `IExternalCodeEditor`
pipeline so double-clicking a script or a console error opens it in nvim at the right line,
generates the `.csproj`/`.sln` files an LSP needs, and keeps them in sync as assemblies change.

- **Assembly:** `NvimUnity.Editor` (`Editor/`) — Editor-only, never in builds.
- **Namespace:** `NvimUnity.Editor`
- **Unity:** 2021.3 or newer.
- **Platforms:** Windows and macOS. Discovery and the sync transport are platform-specific; other
  editor platforms are untested.

Neovim itself is not bundled — install it separately.

---

## Setup

1. Install Neovim.
2. **Edit → Preferences → External Tools → External Script Editor**, and pick Neovim.

Installations are auto-discovered on Windows under `%ProgramFiles%\Neovim\bin\nvim.exe` and
`%LocalAppData%\Programs\Neovim\bin\nvim.exe`. If yours lives elsewhere, use **Browse…** and point
at the `nvim` binary directly.

The package's own settings are drawn in that same External Tools panel, below the editor picker.

---

## Project generation

Two generators are available, selected by **Generator Type**:

| Generator | Output |
|---|---|
| `Classic` (default) | Classic-format `.csproj` — the format Unity itself emits |
| `SdkStyleRoslyn` | SDK-style `.csproj`, for tooling that prefers it |

Settings:

| Setting | Default | Meaning |
|---|---|---|
| Solution Name | `Unity` | Base name for the generated `.sln` |
| Output Directory | *(empty)* | Where to write; empty means the project root |
| Target Framework | *(empty)* | SDK-style only. Empty or `auto` picks `net471` / `netstandard2.1` per assembly |
| Lang Version | *(empty)* | SDK-style only. Empty, `latest` or `auto` uses Unity's own compiler version |
| Include Packages | on | Generate projects for package assemblies too |
| Include Tests | on | Generate projects for test assemblies |
| Auto Sync On Asset Change | on | Regenerate when assemblies change |

Assembly GUIDs are recorded in the config and reused across regenerations, so project identities
stay stable instead of churning every sync.

---

## Menu commands

| Command | Purpose |
|---|---|
| **Tools → NvimUnity → Force SyncProject** | Regenerate all project files now |

---

## What else is in the box

- **Sync server** — a local pipe server that lets a running nvim instance talk to the Editor,
  so files open in the session you already have rather than spawning a new one.
- **Debugger recovery** — clears the wedged state that can follow a detached or crashed debug
  session, which otherwise leaves the Editor unable to attach again.

---

## Notes

Settings are stored in a `ScriptableSingleton`, so they live under the project's `Library/` folder
and are local to your machine rather than committed.

Generated `.csproj`/`.sln` files are build artifacts — the standard Unity `.gitignore` already
excludes them.
