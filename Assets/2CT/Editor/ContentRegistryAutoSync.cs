using System.IO;
using TwoCT.Core;
using UnityEditor;
using UnityEngine;

namespace TwoCT.EditorTools
{
    /// <summary>
    /// Keeps <see cref="ContentRegistry"/> in sync automatically. Whenever a content asset
    /// (card, mythical, boss, pattern, character) under <c>Assets/2CT/Content</c> is added,
    /// deleted, or moved, the registry is rebuilt so new content — e.g. the Hard pattern
    /// variants — is registered for network name-lookup without anyone remembering to run
    /// "2CT ▸ Rebuild Content Registry" by hand.
    ///
    /// Runtime pattern resolution goes through <see cref="ContentRegistry.GetPattern"/> by name;
    /// an unregistered pattern resolves to null and simply never spawns, which is a silent bug.
    /// This guard makes that class of bug impossible in normal authoring.
    /// </summary>
    public class ContentRegistryAutoSync : AssetPostprocessor
    {
        // All content lives here. The registry itself lives under .../Resources, so rebuilding it
        // (which writes ContentRegistry.asset) never re-triggers this callback — no import loop.
        private const string ContentRoot = "Assets/2CT/Content";

        // Coalesce a burst of asset changes in one import into a single rebuild.
        private static bool _pending;

        private static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (BuildPipeline.isBuildingPlayer) return;

            if (!TouchesContent(importedAssets) && !TouchesContent(deletedAssets) &&
                !TouchesContent(movedAssets) && !TouchesContent(movedFromAssetPaths))
                return;

            if (_pending) return;
            _pending = true;

            // Defer: SetDirty/SaveAssets inside the import callback can re-enter the pipeline.
            // delayCall runs on the next editor tick, which is the supported place to save.
            EditorApplication.delayCall += () =>
            {
                _pending = false;
                ContentTools.RebuildRegistry();
            };
        }

        private static bool TouchesContent(string[] paths)
        {
            foreach (var p in paths)
                if (!string.IsNullOrEmpty(p) &&
                    p.StartsWith(ContentRoot) &&
                    string.Equals(Path.GetExtension(p), ".asset", System.StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}
