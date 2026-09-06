using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace GSplat.Sandbox.Editor
{
    /// <summary>
    /// Player builds of the sandbox for the platform regression (TZ E6/E7). Run from the menu or from the shell:
    /// Unity -batchmode -executeMethod GSplat.Sandbox.Editor.BuildScripts.BuildWebGL -quit
    /// The viewer scene is created on demand so the build has something to show.
    /// </summary>
    public static class BuildScripts
    {
        private const string ViewerScenePath = "Assets/Scenes/SplatViewer.unity";
        private const string SpikeScenePath = "Assets/Scenes/SortSpike.unity";

        [MenuItem("GSplat/Build/WebGL (WebGL2)")]
        public static void BuildWebGL()
        {
            EnsureScenes();
            PlayerSettings.WebGL.template = "PROJECT:GSplatViewer";
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.WebGL, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[] { GraphicsDeviceType.OpenGLES3 });
            // TODO(E7-T2): add GraphicsDeviceType.WebGPU in front once the project moves to 6000.6 (production WebGPU).
            Build(BuildTarget.WebGL, "Builds/WebGL");
        }

        [MenuItem("GSplat/Build/Android (Vulkan + GLES3, arm64)")]
        public static void BuildAndroid()
        {
            EnsureScenes();
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });
            Build(BuildTarget.Android, "Builds/Android/GSplatViewer.apk");
        }

        [MenuItem("GSplat/Build/iOS (Xcode project)")]
        public static void BuildIos()
        {
            EnsureScenes();
            Build(BuildTarget.iOS, "Builds/iOS");
        }

        private static void EnsureScenes()
        {
            if (!File.Exists(ViewerScenePath)) GSplat.Editor.ViewerSceneSetup.CreateViewerScene(ViewerScenePath);
            if (!File.Exists(SpikeScenePath)) SpikeSceneSetup.CreateSortSpikeScene();
        }

        private static void Build(BuildTarget target, string location)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(location) ?? ".");
            // Local InnerTest scenes (git-ignored) go into the build when they exist, so a phone build has real worlds to show.
            var scenes = new System.Collections.Generic.List<string> { ViewerScenePath };
            if (Directory.Exists("Assets/Scenes/InnerTest")) scenes.AddRange(Directory.GetFiles("Assets/Scenes/InnerTest", "*.unity"));
            scenes.Add(SpikeScenePath);

            var options = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                target = target,
                locationPathName = location,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            Debug.Log($"GSplat build {target}: {summary.result}, {summary.totalSize / (1024 * 1024)} MB, {summary.totalTime.TotalSeconds:F0} s, errors {summary.totalErrors}");
            if (summary.result != BuildResult.Succeeded && Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}
