using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace GSplat.Editor
{
    /// <summary>
    /// The objects every generated scene starts from (viewer, Niantic samples, InnerTest worlds): a main camera with
    /// fly controls, a splat object bound to an asset, and the "Viewer" object that carries the overlay. Each setup
    /// adds what makes it different on top; the numbers that differ (background, far plane) stay with the caller.
    /// </summary>
    internal static class SceneObjects
    {
        /// <summary>Main camera with URP data and <see cref="SplatFlyCamera"/>; near plane 5 cm so the user can walk up to a splat surface.</summary>
        public static Camera CreateMainCamera(Color background, float farClip)
        {
            var cameraObject = new GameObject("Main Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = background;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = farClip;
            cameraObject.tag = "MainCamera";
            cameraObject.AddComponent<UniversalAdditionalCameraData>();
            cameraObject.AddComponent<SplatFlyCamera>();
            return camera;
        }

        /// <summary>A GameObject named after the asset with a <see cref="GaussianSplatRenderer"/> pointing at it (through SerializedObject, so the scene file records the reference).</summary>
        public static GaussianSplatRenderer CreateSplatObject(GaussianSplatAsset asset)
        {
            var holder = new GameObject(asset.name);
            var renderer = holder.AddComponent<GaussianSplatRenderer>();
            var serialized = new SerializedObject(renderer);
            serialized.FindProperty("asset").objectReferenceValue = asset;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return renderer;
        }

        /// <summary>The "Viewer" object with the debug overlay, the settings applier and the debug menu; callers add UI or quality components to it.</summary>
        public static GameObject CreateViewerObject()
        {
            var viewer = new GameObject("Viewer");
            viewer.AddComponent<SplatDebugOverlay>();
            viewer.AddComponent<SplatSettingsApplier>();
            viewer.AddComponent<SplatDebugMenu>();
            return viewer;
        }
    }
}
