using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GSplat.Sandbox.Editor
{
    /// <summary>
    /// Creates Assets/Scenes/SortSpike.unity: a camera and the SortSpike runner. Menu GSplat/Spikes or -executeMethod from CI.
    /// Built by hand rather than with the package's SceneObjects: the spike needs no fly camera, URP camera data or
    /// overlay, only a plain camera to keep the app alive while the runner measures.
    /// </summary>
    public static class SpikeSceneSetup
    {
        private const string ScenePath = "Assets/Scenes/SortSpike.unity";

        [MenuItem("GSplat/Spikes/Create Sort Spike Scene")]
        public static void CreateSortSpikeScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.1f, 0.1f, 0.12f);
            cameraObject.tag = "MainCamera";

            var runner = new GameObject("Sort Spike");
            runner.AddComponent<SortSpike>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("Created " + ScenePath);
        }
    }
}
