// One button for a mod release: builds the Addressables for Windows, macOS and Linux, zips each with install-mod.ps1,
// installs the Windows build into the game's Mods folder, then switches back to the build target you started on.
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Editor
{
    public static class BuildAllPlatforms
    {
        private static readonly (BuildTarget target, string platform)[] Targets =
        {
            (BuildTarget.StandaloneWindows64, "Windows"),
            (BuildTarget.StandaloneOSX, "OSX"),
            (BuildTarget.StandaloneLinux64, "Linux"),
        };

        [MenuItem("Tools/Build Mod (all platforms)")]
        public static void Run()
        {
            var startTarget = EditorUserBuildSettings.activeBuildTarget;
            var startGroup = BuildPipeline.GetBuildTargetGroup(startTarget);
            try
            {
                foreach (var (target, platform) in Targets)
                {
                    EditorUtility.DisplayProgressBar("Build Mod", $"Building {platform}", 0);
                    if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, target))
                    {
                        Debug.LogError($"[Build Mod] Couldn't switch to {platform}. Is its build support module installed?");
                        return;
                    }
                    if (!SafeAddressablesBuild.Run()) return;
                    if (!InstallMod($"-Platform {platform} -Zip")) return;
                }
                InstallMod("");
                Debug.Log("[Build Mod] All platforms built and zipped; Windows installed.");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                EditorUserBuildSettings.SwitchActiveBuildTarget(startGroup, startTarget);
            }
        }

        private static bool InstallMod(string args)
        {
            var root = Directory.GetParent(Application.dataPath)!.FullName;
            var psi = new ProcessStartInfo("powershell",
                $"-NoProfile -ExecutionPolicy Bypass -File \"{Path.Combine(root, "install-mod.ps1")}\" {args}")
            {
                WorkingDirectory = root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd();
            var error = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode == 0)
            {
                Debug.Log($"[Build Mod] install-mod.ps1 {args}\n{output}");
                return true;
            }
            Debug.LogError($"[Build Mod] install-mod.ps1 {args} failed:\n{error}\n{output}");
            return false;
        }
    }
}
