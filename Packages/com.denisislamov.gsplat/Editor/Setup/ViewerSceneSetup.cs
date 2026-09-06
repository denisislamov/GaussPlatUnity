using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace GSplat.Editor
{
    /// <summary>Creates a ready-to-run viewer scene: camera + fly controls, world loader, UI, quality controller, debug overlay.</summary>
    public static class ViewerSceneSetup
    {
        [MenuItem("GSplat/Setup/Create Viewer Scene")]
        public static void CreateViewerScene()
        {
            string path = EditorUtility.SaveFilePanelInProject("Create GSplat viewer scene", "SplatViewer", "unity", "Where to save the viewer scene");
            if (string.IsNullOrEmpty(path)) return;
            CreateViewerScene(path);
        }

        /// <summary>Rewrites Assets/Scenes/SplatViewer.unity with the current component set (batch mode helper).</summary>
        public static void RegenerateDefaultViewerScene()
        {
            CreateViewerScene("Assets/Scenes/SplatViewer.unity");
        }

        public static void CreateViewerScene(string path)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.05f, 0.07f);
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 500f;
            cameraObject.tag = "MainCamera";
            cameraObject.AddComponent<UniversalAdditionalCameraData>();
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<SplatFlyCamera>();
            cameraObject.transform.position = new Vector3(0f, 1.6f, -3f);

            var world = new GameObject("World");
            world.AddComponent<WorldLoader>();

            var viewer = new GameObject("Viewer");
            viewer.AddComponent<SplatViewerUi>();
            viewer.AddComponent<SplatQualityController>();
            viewer.AddComponent<SplatDebugOverlay>();

            EditorSceneManager.SaveScene(scene, path);
            Debug.Log($"GSplat: viewer scene created at {path}. Set 'World Url' on the World object (a descriptor .json or a .spz URL) and press Play.");
        }
    }
}
