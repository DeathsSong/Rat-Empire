#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RatHabitat.Editor
{
    public static class RatHabitatBuildMenu
    {
        private static readonly string[] Scenes = { "Assets/Scenes/Main.unity" };

        [MenuItem("Rat Empire/Build Android APK")]
        public static void BuildAndroid()
        {
            string path = EditorUtility.SaveFilePanel("Build Rat Empire Android APK", "", "RatEmpire-VerticalSlice.apk", "apk");
            if (string.IsNullOrEmpty(path)) return;
            EditorUserBuildSettings.buildAppBundle = false;
            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = path,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            ShowResult(report, path);
        }

        [MenuItem("Rat Empire/Build Windows Test Player")]
        public static void BuildWindows()
        {
            string path = EditorUtility.SaveFilePanel("Build Rat Empire Windows Player", "", "RatEmpire-VerticalSlice.exe", "exe");
            if (string.IsNullOrEmpty(path)) return;
            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = path,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            ShowResult(report, path);
        }

        private static void ShowResult(BuildReport report, string path)
        {
            if (report.summary.result == BuildResult.Succeeded)
            {
                EditorUtility.DisplayDialog("Rat Empire build complete", "Build created at:\n" + path + "\n\nSize: " + (report.summary.totalSize / (1024f * 1024f)).ToString("0.0") + " MB", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Rat Empire build failed", report.summary.result + "\nCheck the Console for details.", "OK");
            }
        }
    }
}
#endif
