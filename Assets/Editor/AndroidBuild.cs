#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RatHabitat.Editor
{
    /// <summary>
    /// Reproducible development APK entry point for phone smoke testing.
    /// The build is deliberately Main-only; InteractionSmokeTest remains an
    /// optional editor/test scene in Build Settings but is not the app entry.
    /// </summary>
    public static class AndroidBuild
    {
        private const string MainScenePath = "Assets/Scenes/Main.unity";
        private const string OutputPath = "Builds/Android/RatHabitat-development.apk";

        [MenuItem("Rat Habitat/Build Android Development APK")]
        public static void BuildDevelopmentApk()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string mainSceneAbsolutePath = Path.Combine(projectRoot, MainScenePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(mainSceneAbsolutePath))
            {
                throw new InvalidOperationException("Required Main scene was not found at " + MainScenePath + ".");
            }

            string outputPath = Path.Combine(projectRoot, OutputPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled && !string.IsNullOrEmpty(scene.path))
                .Select(scene => scene.path)
                .ToArray();
            if (!scenes.Contains(MainScenePath))
            {
                throw new InvalidOperationException("Main scene is not enabled in Editor Build Settings.");
            }

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android &&
                !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
            {
                throw new InvalidOperationException("Unity could not switch the active build target to Android.");
            }

            // Keep the build deterministic even when serialized Player Settings were authored
            // before Android Build Support was installed or the editor switched targets.
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            Debug.Log("[Rat Habitat] Android build target=" + EditorUserBuildSettings.activeBuildTarget +
                " architectures=" + PlayerSettings.Android.targetArchitectures);

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = new[] { MainScenePath },
                locationPathName = outputPath,
                targetGroup = BuildTargetGroup.Android,
                target = BuildTarget.Android,
                options = BuildOptions.Development | BuildOptions.AllowDebugging
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    "Android development APK build failed: " + report.summary.result +
                    " after " + report.summary.totalErrors + " error(s).");
            }

            Debug.Log("[Rat Habitat] Android development APK built: " + outputPath +
                " (" + report.summary.totalSize + " bytes).");
        }
    }
}
#endif
