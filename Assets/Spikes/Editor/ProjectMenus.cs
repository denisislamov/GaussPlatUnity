using GSplat.Editor;
using UnityEditor;
using UnityEngine;

namespace GSplat.Sandbox.Editor
{
    /// <summary>
    /// The menu items that know this example project's folders: where the Niantic samples and the InnerTest worlds are,
    /// where the generated scenes go. The package's tools (ViewerSceneSetup, SampleSceneSetup, WorldDescriptorWriter)
    /// take folders as parameters and stay project-agnostic. Also the batch entry points the scripts call:
    ///   Unity -batchmode -executeMethod GSplat.Sandbox.Editor.ProjectMenus.RegenerateViewerScene -quit
    /// </summary>
    public static class ProjectMenus
    {
        public const string NianticFolder = "Assets/Samples/Niantic";
        public const string InnerTestFolder = "Assets/Samples/InnerTest";
        public const string ViewerScenePath = "Assets/Scenes/SplatViewer.unity";
        public const string NianticScenePath = "Assets/Scenes/NianticSamples.unity";
        public const string InnerTestScenesFolder = "Assets/Scenes/InnerTest";

        [MenuItem("GSplat/Project/Regenerate Viewer Scene")]
        public static void RegenerateViewerScene()
        {
            ViewerSceneSetup.CreateViewerScene(ViewerScenePath);
        }

        [MenuItem("GSplat/Project/Create Niantic Samples Scene")]
        public static void CreateNianticSamplesScene()
        {
            SampleSceneSetup.CreateSceneFromFolder(NianticFolder, NianticScenePath);
        }

        [MenuItem("GSplat/Project/Create InnerTest Scenes (one per world)")]
        public static void CreateInnerTestScenes()
        {
            SampleSceneSetup.CreateScenePerWorld(InnerTestFolder, InnerTestScenesFolder);
        }

        /// <summary>InnerTest exports use LDF axes (x left, y down, z forward).</summary>
        [MenuItem("GSplat/Project/Reimport InnerTest Samples As LDF")]
        public static void ReimportInnerTestSamplesAsLdf()
        {
            SampleSceneSetup.SetCoordinateSystem(InnerTestFolder, SplatCoordinateSystem.Ldf);
        }

        [MenuItem("GSplat/Project/Reimport Niantic Samples With SH 3")]
        public static void ReimportNianticSamplesWithSh3()
        {
            SampleSceneSetup.ReimportWithShDegree(NianticFolder, ShMath.MaxDegree);
        }

        [MenuItem("GSplat/Project/Reimport Samples With Importance-Ordered Chunks")]
        public static void ReimportSamplesWithImportanceOrder()
        {
            SampleSceneSetup.ReimportWithImportanceOrder(NianticFolder);
            SampleSceneSetup.ReimportWithImportanceOrder(InnerTestFolder);
        }

        [MenuItem("GSplat/Project/Write World Descriptors for InnerTest Samples")]
        public static void WriteInnerTestDescriptors()
        {
            WorldDescriptorWriter.WriteDescriptorsUnder(InnerTestFolder);
        }

        /// <summary>
        /// Builds the scene of the package sample "Study Room 100k": the world folder is copied to a temporary folder
        /// inside Assets so the scene references a fresh asset GUID, then the scene, the file and their .meta files are
        /// copied into Samples~ of the package. Run from batch mode when the sample data or the scene layout changes.
        /// </summary>
        public static void BuildStudyRoomSample()
        {
            const string source = "Assets/Samples/InnerTest/study_room/study_room_100k.spz";
            const string tempFolder = "Assets/SampleBuild_Temp";
            const string tempWorld = tempFolder + "/Study Room 100k";
            const string target = "Packages/com.denisislamov.gsplat/Samples~/Study Room 100k";

            System.IO.Directory.CreateDirectory(tempWorld);
            System.IO.File.Copy(source, tempWorld + "/study_room_100k.spz", true);
            System.IO.File.Copy(target + "/study_room_100k.spz.meta", tempWorld + "/study_room_100k.spz.meta", true);
            AssetDatabase.Refresh();

            SampleSceneSetup.CreateScenePerWorld(tempFolder, tempFolder);
            AssetDatabase.Refresh();

            System.IO.Directory.CreateDirectory(target);
            foreach (string name in new[] { "Study Room 100k.unity", "Study Room 100k.unity.meta" })
            {
                System.IO.File.Copy(tempFolder + "/" + name, target + "/" + name, true);
            }

            AssetDatabase.DeleteAsset(tempFolder);
            Debug.Log("GSplat: sample scene written to " + target);
        }
    }
}
