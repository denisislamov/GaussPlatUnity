using System;
using GLTFast;
using UnityEngine;

namespace GSplat
{
    /// <summary>
    /// TZ E1-T6 / E9-T3: loads the world's collider GLB with glTFast, keeps only the geometry as invisible
    /// MeshColliders and switches the camera to walk mode. Compiled only when com.unity.cloud.gltfast is in the
    /// project (see GSplat.Gltf.asmdef), so the core package has no hard dependency on it.
    /// </summary>
    [AddComponentMenu("GSplat/Collider Loader (glTFast)")]
    public sealed class SplatColliderLoader : MonoBehaviour
    {
        [SerializeField] private WorldLoader loader;
        [SerializeField] private SplatFlyCamera flyCamera;

        private GameObject colliderRoot;

        private void OnEnable()
        {
            if (loader == null) loader = FindFirstObjectByType<WorldLoader>();
            if (flyCamera == null) flyCamera = FindFirstObjectByType<SplatFlyCamera>();
            if (loader != null) loader.ColliderRequested += OnColliderRequested;
        }

        private void OnDisable()
        {
            if (loader != null) loader.ColliderRequested -= OnColliderRequested;
        }

        private void OnColliderRequested(string url, Transform parent)
        {
            _ = LoadAsync(url, parent);
        }

        public async Awaitable LoadAsync(string url, Transform parent)
        {
            if (colliderRoot != null) Destroy(colliderRoot);
            colliderRoot = new GameObject("Collider (GLB)");
            colliderRoot.transform.SetParent(parent, false);

            var gltf = new GltfImport();
            bool loaded;
            try
            {
                loaded = await gltf.Load(new Uri(url));
                if (loaded) loaded = await gltf.InstantiateMainSceneAsync(colliderRoot.transform);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"GSplat: collider {url} could not be loaded ({e.Message}); the camera stays in fly mode.");
                return;
            }

            if (!loaded)
            {
                Debug.LogWarning($"GSplat: collider {url} could not be loaded; the camera stays in fly mode.");
                return;
            }

            // The GLB is physics only: hide its renderers, give every mesh a MeshCollider.
            foreach (MeshFilter filter in colliderRoot.GetComponentsInChildren<MeshFilter>())
            {
                var meshRenderer = filter.GetComponent<MeshRenderer>();
                if (meshRenderer != null) meshRenderer.enabled = false;
                var collider = filter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
            }

            if (flyCamera != null) flyCamera.WalkMode = true;
        }
    }
}
