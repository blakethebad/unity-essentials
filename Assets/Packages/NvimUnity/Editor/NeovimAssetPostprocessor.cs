using System;
using System.IO;
using Unity.CodeEditor;
using UnityEditor;

namespace NvimUnity.Editor
{
    // Returning true tells Unity's own csproj generator to back off while Neovim is the editor.
    internal class NeovimAssetPostprocessor : AssetPostprocessor
    {
        public static bool OnPreGeneratingCSProjectFiles()
        {
            return IsNeovimSelected();
        }

        // Runs after every asset import batch. Unity's SyncIfNeeded misses in-editor moves, so
        // regeneration happens here instead. SyncProject only rewrites files that changed.
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

            // Writes .csproj/.sln only, so it cannot re-trigger this callback. Null during reloads.
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
