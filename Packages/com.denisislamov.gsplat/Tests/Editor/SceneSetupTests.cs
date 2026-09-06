using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GSplat.Tests
{
    /// <summary>
    /// The scene generators write real scene files; these tests build them into a temporary folder and check the
    /// component set, so a refactoring of the setup code cannot silently drop the fly camera or the overlay.
    /// </summary>
    public sealed class SceneSetupTests
    {
        private const string TempFolder = "Assets/GSplatSceneSetupTests_Temp";

        [TearDown]
        public void DeleteTempFolder()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (AssetDatabase.IsValidFolder(TempFolder)) AssetDatabase.DeleteAsset(TempFolder);
        }

        [Test]
        public void ViewerSceneHasCameraWorldAndViewerObjects()
        {
            Directory.CreateDirectory(TempFolder);
            string path = TempFolder + "/Viewer.unity";
            GSplat.Editor.ViewerSceneSetup.CreateViewerScene(path);
            Assert.IsTrue(File.Exists(path), "scene file written");

            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            Camera camera = Camera.main;
            Assert.IsNotNull(camera, "main camera");
            Assert.AreEqual(new Vector3(0f, 1.6f, -3f), camera.transform.position);
            Assert.AreEqual(500f, camera.farClipPlane);
            Assert.AreEqual(0.05f, camera.nearClipPlane, 1e-6f);
            Assert.IsNotNull(camera.GetComponent<SplatFlyCamera>(), "fly camera");
            Assert.IsNotNull(camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>(), "URP camera data");
            Assert.IsNotNull(camera.GetComponent<AudioListener>(), "audio listener");

            GameObject world = GameObject.Find("World");
            Assert.IsNotNull(world, "World object");
            Assert.IsNotNull(world.GetComponent<WorldLoader>(), "world loader");
            Assert.IsNotNull(world.GetComponent<WebPageBridge>(), "web page bridge (index.html sends messages to \"World\")");

            GameObject viewer = GameObject.Find("Viewer");
            Assert.IsNotNull(viewer, "Viewer object");
            Assert.IsNotNull(viewer.GetComponent<SplatViewerUi>(), "viewer UI");
            Assert.IsNotNull(viewer.GetComponent<SplatQualityController>(), "quality controller");
            Assert.IsNotNull(viewer.GetComponent<SplatDebugOverlay>(), "debug overlay");
            Assert.AreEqual(scene.path, path);
        }

        [Test]
        public void InnerTestWorldSceneHasSplatsCubeAndSceneSwitchCanvas()
        {
            const string worldsFolder = "Assets/Samples/InnerTest";
            if (!Directory.Exists(worldsFolder) || Directory.GetDirectories(worldsFolder).Length == 0) Assert.Ignore("No InnerTest worlds in this checkout.");

            GSplat.Editor.SampleSceneSetup.CreateScenePerWorld(worldsFolder, TempFolder);
            string[] scenes = Directory.GetFiles(TempFolder, "*.unity");
            Assert.AreEqual(Directory.GetDirectories(worldsFolder).Length, scenes.Length, "one scene per world folder");

            EditorSceneManager.OpenScene(scenes[0].Replace('\\', '/'), OpenSceneMode.Single);
            Camera camera = Camera.main;
            Assert.IsNotNull(camera, "main camera");
            Assert.AreEqual(Vector3.zero, camera.transform.position, "InnerTest worlds are viewed from the origin");
            Assert.AreEqual(65f, camera.fieldOfView);
            Assert.AreEqual(200f, camera.farClipPlane);
            Assert.IsNotNull(camera.GetComponent<SplatFlyCamera>(), "fly camera");

            GaussianSplatRenderer[] renderers = Object.FindObjectsByType<GaussianSplatRenderer>(FindObjectsSortMode.None);
            Assert.AreEqual(1, renderers.Length, "exactly one world per scene");
            Assert.IsNotNull(renderers[0].Asset, "renderer bound to its asset");
            Assert.IsTrue(renderers[0].Asset.name.Contains("_500k"), "the 500k level is the one shown: " + renderers[0].Asset.name);

            GameObject viewer = GameObject.Find("Viewer");
            Assert.IsNotNull(viewer.GetComponent<SplatDebugOverlay>(), "debug overlay");
            Assert.IsNotNull(viewer.GetComponent<DeviceQualityApplier>(), "device quality applier");

            var cube = GameObject.Find("Reference Cube (URP Lit)");
            Assert.IsNotNull(cube, "URP cube");
            Assert.AreEqual("Universal Render Pipeline/Lit", cube.GetComponent<MeshRenderer>().sharedMaterial.shader.name);

            NextSceneButton button = Object.FindFirstObjectByType<NextSceneButton>();
            Assert.IsNotNull(button, "scene switch button");
            Assert.IsNotNull(button.GetComponent<UnityEngine.UI.Button>(), "uGUI button on the same object");
            Assert.IsNotNull(button.GetComponentInParent<SafeAreaPanel>(), "button sits inside the safe-area panel");
            Assert.AreEqual("Next scene", button.GetComponentInChildren<UnityEngine.UI.Text>().text);
        }
    }
}
