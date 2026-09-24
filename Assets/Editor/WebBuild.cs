#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RatHabitat.Editor
{
    public static class WebBuild
    {
        private const string MainScenePath = "Assets/Scenes/Main.unity";
        private const string OutputPath = "Builds/WebGL";

        [MenuItem("Rat Habitat/Build/Build Phone WebGL")]
        public static void BuildPhoneWebGL()
        {
            if (!File.Exists(MainScenePath))
                throw new FileNotFoundException("The Main scene was not found.", MainScenePath);

            string outputPath = Path.GetFullPath(OutputPath);
            Directory.CreateDirectory(outputPath);

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL &&
                !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL))
            {
                throw new InvalidOperationException("Unity could not switch the active build target to WebGL.");
            }

            WebGLCompressionFormat previousCompression = PlayerSettings.WebGL.compressionFormat;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;

            try
            {
                BuildPlayerOptions options = new BuildPlayerOptions
                {
                    scenes = new[] { MainScenePath },
                    locationPathName = outputPath,
                    target = BuildTarget.WebGL,
                    targetGroup = BuildTargetGroup.WebGL,
                    options = BuildOptions.Development | BuildOptions.AllowDebugging
                };

                BuildReport report = BuildPipeline.BuildPlayer(options);
                if (report.summary.result != BuildResult.Succeeded)
                    throw new InvalidOperationException($"WebGL build failed with result {report.summary.result}.");

                Debug.Log($"[Rat Habitat] Phone WebGL build completed: {outputPath} ({report.summary.totalSize} bytes).");
            }
            finally
            {
                PlayerSettings.WebGL.compressionFormat = previousCompression;
            }
        }
    }
}
#endif
