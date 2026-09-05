using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace GSplat.Editor
{
    /// <summary>
    /// Builds a scene that shows every GaussianSplatAsset found under a folder, side by side, with the fly camera and
    /// the debug overlay. Used for the Niantic sample scenes (Assets/Samples/Niantic) but works for any folder.
    /// </summary>
    public static class SampleSceneSetup
    {
        private const string SamplesFolder = "Assets/Samples/Niantic";
        private const string ScenePath = "Assets/Scenes/NianticSamples.unity";

        [MenuItem("GSplat/Setup/Create Niantic Samples Scene")]
        public static void CreateNianticSamplesScene()
        {
            CreateSceneFromFolder(SamplesFolder, ScenePath);
        }

        public static void CreateSceneFromFolder(string folder, string scenePath)
        {
            // New scene first: NewScene(Single) unloads unreferenced assets, which would destroy assets loaded before it.
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            List<GaussianSplatAsset> assets = FindAssets(folder);
            if (assets.Count == 0)
            {
                Debug.LogError($"GSplat: no GaussianSplatAsset under {folder}. Put .spz/.ply files there first.");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            var cameraObject = new GameObject("Main Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.08f, 0.1f);
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 500f;
            cameraObject.tag = "MainCamera";
            cameraObject.AddComponent<UniversalAdditionalCameraData>();
            cameraObject.AddComponent<SplatFlyCamera>();

            var viewer = new GameObject("Viewer");
            viewer.AddComponent<SplatDebugOverlay>();

            // Scenes are laid out along X with a gap, each standing on y = 0 by its bounds.
            float cursorX = 0f;
            float largestSize = 0f;
            for (int assetIndex = 0; assetIndex < assets.Count; assetIndex++)
            {
                GaussianSplatAsset asset = assets[assetIndex];
                var holder = new GameObject(asset.name);
                var renderer = holder.AddComponent<GaussianSplatRenderer>();
                var serialized = new SerializedObject(renderer);
                serialized.FindProperty("asset").objectReferenceValue = asset;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Bounds bounds = asset.Bounds;
                float size = Mathf.Max(bounds.size.x, bounds.size.z);
                cursorX += size * 0.5f;
                holder.transform.position = new Vector3(cursorX - bounds.center.x, -bounds.min.y, -bounds.center.z);
                cursorX += size * 0.5f + size * 0.3f;
                largestSize = Mathf.Max(largestSize, bounds.size.magnitude);
            }

            float totalWidth = cursorX;
            cameraObject.transform.position = new Vector3(totalWidth * 0.5f, largestSize * 0.4f, -largestSize * 1.2f);
            cameraObject.transform.LookAt(new Vector3(totalWidth * 0.5f, largestSize * 0.2f, 0f));

            EditorSceneManager.SaveScene(scene, scenePath);
            Debug.Log($"GSplat: sample scene with {assets.Count} asset(s) saved to {scenePath}.");
        }

        private static List<GaussianSplatAsset> FindAssets(string folder)
        {
            // By file, not by "t:GaussianSplatAsset": the type search index is not reliable right after a batch import.
            var result = new List<GaussianSplatAsset>();
            if (!System.IO.Directory.Exists(folder)) return result;
            foreach (string file in System.IO.Directory.GetFiles(folder))
            {
                string extension = System.IO.Path.GetExtension(file).ToLowerInvariant();
                if (extension != ".spz" && extension != ".ply") continue;

                var asset = AssetDatabase.LoadAssetAtPath<GaussianSplatAsset>(file.Replace('\\', '/'));
                if (asset != null) result.Add(asset);
            }

            result.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return result;
        }
    }
}
