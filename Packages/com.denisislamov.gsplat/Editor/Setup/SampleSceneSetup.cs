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

        /// <summary>InnerTest exports use LDF axes (x left, y down, z forward); set that on every splat file under the InnerTest folder and re-import.</summary>
        [MenuItem("GSplat/Setup/Reimport InnerTest Samples As LDF")]
        public static void ReimportInnerTestSamplesAsLdf()
        {
            SetCoordinateSystem("Assets/Samples/InnerTest", SplatCoordinateSystem.Ldf);
        }

        public static void SetCoordinateSystem(string folder, SplatCoordinateSystem coordinateSystem)
        {
            if (!System.IO.Directory.Exists(folder)) return;
            foreach (string file in System.IO.Directory.GetFiles(folder, "*.*", System.IO.SearchOption.AllDirectories))
            {
                string extension = System.IO.Path.GetExtension(file).ToLowerInvariant();
                if (extension != ".spz" && extension != ".ply") continue;

                var importer = AssetImporter.GetAtPath(file.Replace('\\', '/')) as SplatImporterBase;
                if (importer == null || importer.Options.SourceCoordinateSystem == coordinateSystem) continue;
                importer.Options.SourceCoordinateSystem = coordinateSystem;
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
                Debug.Log($"GSplat: reimported as {coordinateSystem}: {file}");
            }
        }

        /// <summary>InnerTest exports are for internal testing only (their license does not allow redistribution): the folder is git-ignored.</summary>
        [MenuItem("GSplat/Setup/Create InnerTest Scenes (one per world)")]
        public static void CreateInnerTestScenes()
        {
            CreateScenePerWorld("Assets/Samples/InnerTest", "Assets/Scenes/InnerTest");
        }

        /// <summary>
        /// One scene per world folder, camera at the origin (InnerTest generation viewpoint), device quality applied in
        /// players. Three 500k worlds in one scene were too much for a mid-range phone.
        /// </summary>
        public static void CreateScenePerWorld(string folder, string sceneFolder)
        {
            if (!System.IO.Directory.Exists(folder))
            {
                Debug.LogError("GSplat: no folder " + folder);
                return;
            }

            System.IO.Directory.CreateDirectory(sceneFolder);
            int created = 0;
            foreach (string worldFolder in System.IO.Directory.GetDirectories(folder))
            {
                string name = System.IO.Path.GetFileName(worldFolder);
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                List<GaussianSplatAsset> assets = FindAssets(worldFolder);
                if (assets.Count == 0) continue;

                var cameraObject = new GameObject("Main Camera");
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.02f, 0.02f, 0.03f);
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 200f;
                camera.fieldOfView = 65f;
                cameraObject.tag = "MainCamera";
                cameraObject.AddComponent<UniversalAdditionalCameraData>();
                cameraObject.AddComponent<SplatFlyCamera>();

                var holder = new GameObject(assets[0].name);
                var renderer = holder.AddComponent<GaussianSplatRenderer>();
                var serialized = new SerializedObject(renderer);
                serialized.FindProperty("asset").objectReferenceValue = assets[0];
                serialized.ApplyModifiedPropertiesWithoutUndo();

                var viewer = new GameObject("Viewer");
                viewer.AddComponent<SplatDebugOverlay>();
                viewer.AddComponent<DeviceQualityApplier>();
                viewer.AddComponent<SplatSceneMenu>();

                AddReferenceGeometry();

                string scenePath = $"{sceneFolder}/{name}.unity";
                EditorSceneManager.SaveScene(scene, scenePath);
                created++;
            }

            Debug.Log($"GSplat: {created} scene(s) written to {sceneFolder}.");
        }

        /// <summary>Re-imports every .spz/.ply under the samples folder keeping SH degree 3 (the importer default is 0, tuned for phones).</summary>
        [MenuItem("GSplat/Setup/Reimport Niantic Samples With SH 3")]
        public static void ReimportSamplesWithSh3()
        {
            foreach (string file in System.IO.Directory.GetFiles(SamplesFolder))
            {
                string extension = System.IO.Path.GetExtension(file).ToLowerInvariant();
                if (extension != ".spz" && extension != ".ply") continue;

                var importer = AssetImporter.GetAtPath(file.Replace('\\', '/')) as SplatImporterBase;
                if (importer == null) continue;
                importer.Options.TargetShDegree = ShMath.MaxDegree;
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
                Debug.Log("GSplat: reimported with SH 3: " + file);
            }
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

            // Phone captures include a sphere of background splats hundreds of meters across, so the camera starts
            // inside the first scene, near its center, looking along +Z; the far plane follows the bounds.
            GaussianSplatAsset first = assets[0];
            float firstSize = Mathf.Max(first.Bounds.size.x, first.Bounds.size.z);
            cameraObject.transform.position = new Vector3(firstSize * 0.5f, -first.Bounds.min.y + first.Bounds.center.y + 0.5f, -first.Bounds.center.z - first.Bounds.size.z * 0.05f);
            cameraObject.transform.rotation = Quaternion.identity;
            camera.farClipPlane = Mathf.Max(500f, largestSize * 2f);

            EditorSceneManager.SaveScene(scene, scenePath);
            Debug.Log($"GSplat: sample scene with {assets.Count} asset(s) saved to {scenePath}.");
        }

        /// <summary>
        /// One asset per folder: when a folder holds several quality levels of the same world (InnerTest exports:
        /// 100k/150k/500k/full_res) the 500k one is taken, else the largest. By file, not by "t:GaussianSplatAsset":
        /// the type search index is not reliable right after a batch import.
        /// </summary>
        /// <summary>A lit URP cube and a light in front of the spawn: shows depth compositing of splats with ordinary geometry.</summary>
        private static void AddReferenceGeometry()
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Reference Cube (URP Lit)";
            cube.transform.position = new Vector3(0.6f, -0.6f, 2f);
            cube.transform.localScale = Vector3.one * 0.4f;
            cube.transform.rotation = Quaternion.Euler(0f, 30f, 0f);
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.9f, 0.45f, 0.15f) };
            cube.GetComponent<MeshRenderer>().sharedMaterial = material;

            var lightObject = new GameObject("Directional Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        private static List<GaussianSplatAsset> FindAssets(string folder)
        {
            var result = new List<GaussianSplatAsset>();
            if (!System.IO.Directory.Exists(folder)) return result;

            var folders = new List<string> { folder };
            folders.AddRange(System.IO.Directory.GetDirectories(folder, "*", System.IO.SearchOption.AllDirectories));
            foreach (string directory in folders)
            {
                GaussianSplatAsset chosen = null;
                foreach (string file in System.IO.Directory.GetFiles(directory))
                {
                    string extension = System.IO.Path.GetExtension(file).ToLowerInvariant();
                    if (extension != ".spz" && extension != ".ply") continue;

                    var asset = AssetDatabase.LoadAssetAtPath<GaussianSplatAsset>(file.Replace('\\', '/'));
                    if (asset == null) continue;
                    bool preferred = file.Contains("_500k");
                    if (chosen == null || preferred || (!chosen.name.Contains("_500k") && asset.SplatCount > chosen.SplatCount)) chosen = asset;
                }

                if (chosen != null) result.Add(chosen);
            }

            result.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return result;
        }
    }
}
