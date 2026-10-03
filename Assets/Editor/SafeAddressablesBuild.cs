// Addressables build that works around a package bug: folder entries' [NonSerialized] SubAssets can come back null
// after a domain reload (e.g. switching build target), and the catalog step then throws a NullReferenceException on
// `entry.IsFolder && entry.SubAssets.Count`, even for groups excluded from the build. Seen on LynkMod's folder entry.
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Editor
{
    public static class SafeAddressablesBuild
    {
        [MenuItem("Tools/Build Addressables (fix null SubAssets)")]
        public static void RunFromMenu() => Run();

        /// Returns true if the build succeeded.
        public static bool Run()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            foreach (var group in settings.groups)
            {
                if (group == null) continue;
                foreach (var entry in group.entries)
                {
                    if (entry == null || entry.SubAssets != null) continue;
                    entry.SubAssets = new List<AddressableAssetEntry>();
                    Debug.Log($"[Build Addressables] Reset null SubAssets on '{group.Name}' / {entry.AssetPath}");
                }
            }

            AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
            if (string.IsNullOrEmpty(result.Error))
            {
                Debug.Log($"[Build Addressables] Build succeeded for {EditorUserBuildSettings.activeBuildTarget}.");
                return true;
            }
            Debug.LogError($"[Build Addressables] Build failed: {result.Error}");
            return false;
        }
    }
}
