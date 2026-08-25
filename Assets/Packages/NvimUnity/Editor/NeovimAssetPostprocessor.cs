using System;
using System.IO;
using Unity.CodeEditor;
using UnityEditor;

namespace NvimUnity.Editor
{
    // Mirrors RiderAssetPostprocessor. Returning true here tells Unity's built-in
    // csproj generator to back off when Neovim is the selected external editor.
    internal class NeovimAssetPostprocessor : AssetPostprocessor
    {
        public static bool OnPreGeneratingCSProjectFiles()
        {
            return IsNeovimSelected();
        }

        // Fires for every asset import batch (imports, moves/renames, deletes). Unity's code-editor
        // SyncIfNeeded path misses in-editor moves, so this is the reliable universal hook to keep
        // csproj/sln regenerated after .cs/.asmdef/.asmref changes. Idempotent-cheap: SyncProject
        // only rewrites files whose content changed.
        private static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (!IsNeovimSelected()) return;
            if (!NvimUnityConfig.instance.AutoSyncOnAssetChange) return;

            if (!HasRelevantAsset(importedAssets)
                && !HasRelevantAsset(deletedAssets)
                && !HasRelevantAsset(movedAssets)
                && !HasRelevantAsset(movedFromAssetPaths))
                return;

            // SyncProject writes .csproj/.sln (not assets), so it cannot re-trigger this callback.
            // Called once per batch, not per file. Null generator (assembly reload) -> no-op.
            NeovimSyncServer.ActiveGenerator?.SyncProject();
        }

        private static bool IsNeovimSelected()
        {
            var path = CodeEditor.CurrentEditorInstallation;
            if (string.IsNullOrEmpty(path)) return false;
            var name = Path.GetFileName(path).ToLowerInvariant();
            return name == "nvim.exe" || name == "nvim";
        }

        private static bool HasRelevantAsset(string[] paths)
        {
            if (paths == null) return false;
            foreach (var p in paths)
            {
                if (string.IsNullOrEmpty(p)) continue;
                if (p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                    || p.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase)
                    || p.EndsWith(".asmref", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
