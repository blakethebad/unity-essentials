# unity-essentials

A library of reusable systems for my Unity projects, published as independent UPM packages.

Each system is its own package with its own version and tag, so projects install only what they
need and upgrade one package at a time. Nothing here depends on anything else here — every package
is self-contained.

This repo is also a working Unity project: the packages are embedded under `Packages/`, so they
are developed and tested here and consumed by git URL elsewhere.

---

## Packages

| Package | Install URL |
|---|---|
| **Essentials StateManager**<br>Enum-keyed FSM with a deny-all transition table | `https://github.com/blakethebad/unity-essentials.git?path=/Packages/com.unityessentials.states#Essentials.StateManager/1.0.0` |
| **Essentials UISystem**<br>Small uGUI window system | `https://github.com/blakethebad/unity-essentials.git?path=/Packages/com.unityessentials.ui#Essentials.UISystem/1.0.0` |
| **Essentials Utils**<br>Timer, logging, singletons, EventBus, AllocationCounter | `https://github.com/blakethebad/unity-essentials.git?path=/Packages/com.unityessentials.utilities#Essentials.Utils/1.0.0` |
| **Essentials Extensions**<br>Extension methods for Unity/BCL types | `https://github.com/blakethebad/unity-essentials.git?path=/Packages/com.unityessentials.extensions#Essentials.Extensions/1.0.0` |
| **Essentials ServiceLocator**<br>Static, allocation-free service locator | `https://github.com/blakethebad/unity-essentials.git?path=/Packages/com.unityessentials.services#Essentials.ServiceLocator/1.0.0` |
| **Essentials InverseCollider**<br>Keeps rigidbodies *inside* a volume | `https://github.com/blakethebad/unity-essentials.git?path=/Packages/com.unityessentials.colliders#Essentials.InverseCollider/1.0.0` |
| **Essentials Haptics**<br>Cross-platform haptics, iOS + Android | `https://github.com/blakethebad/unity-essentials.git?path=/Packages/com.unityessentials.haptics#Essentials.Haptics/1.0.0` |
| **NvimUnity**<br>Neovim as Unity's external script editor | `https://github.com/blakethebad/unity-essentials.git?path=/Packages/com.nvimunity.editor#NvimUnity/1.0.0` |

### Installing

**Package Manager → + → Add package from git URL**, paste a URL from the table.

Or add it to the consuming project's `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.unityessentials.states": "https://github.com/blakethebad/unity-essentials.git?path=/Packages/com.unityessentials.states#Essentials.StateManager/1.0.0"
  }
}
```

**Always keep the `#tag`.** Without it the dependency tracks `main` and will move underneath you.
To upgrade, change the tag and let Package Manager re-resolve.

---

## Requirements

Unity 6000.0 or newer for the `Essentials.*` packages; NvimUnity supports 2021.3 or newer.

Package-specific dependencies are declared in each `package.json` and resolved automatically.
`Essentials UISystem` pulls in `com.unity.ugui`; the rest need only built-in engine modules.

One thing Package Manager cannot do for you: **`Essentials Utils`' `SingletonScriptableObject<T>`
loads its asset from a `Resources/` folder, which has to exist in your project.** The package ships
none, by design — the asset is yours to author.

---

## Tests

Every package ships its unit tests, and **they do not compile in your project.** The test
assemblies are gated on `UNITY_INCLUDE_TESTS`, which Unity leaves undefined for immutable
packages — anything installed from a git URL. The files sit inert in the package cache; no test
code enters your compilation, your builds, or your Test Runner.

If you *want* to run them, opt in from the consuming project:

```json
{
  "dependencies": {
    "com.unity.test-framework": "1.7.0"
  },
  "testables": [
    "com.unityessentials.states"
  ]
}
```

---

## Versioning and releases

Each package is versioned independently and tagged `<Package.Name>/<semver>`, e.g.
`Essentials.StateManager/1.0.0`. Per-package changelogs live beside each `package.json`.

A git tag marks a commit of the whole repo, so a tag cut for one package also carries whatever
state the others are in. That is harmless — `?path=` means a consumer receives only the subfolder
it asked for — but it does mean each package's `CHANGELOG.md`, not the tag, is the record of what
actually changed.

---

## License

MIT — see [LICENSE.md](LICENSE.md). Each package carries its own copy, since installing by git URL
delivers only that package's folder.
