using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace GSplat.Tests
{
    /// <summary>
    /// Golden images of real scenes (a Niantic sample and one InnerTest world) at a fixed pose. The synthetic
    /// 400-splat scene in <see cref="GoldenImageTests"/> is too small to notice a wrong SH band, a broken chunk
    /// boundary or a sort key that drifts on a 500k cloud; these two do. CPU sorter only: the GPU counting sort
    /// scatters equal keys in a nondeterministic order and a dense scene has many equal keys, which would make the
    /// comparison flaky. The GPU path is covered by the synthetic golden images and by GpuAndCpuSortersRenderTheSameImage.
    /// </summary>
    public sealed class RealSceneGoldenTests
    {
        private const int Size = 512;

        [UnityTest]
        public IEnumerator HornedLizard512()
        {
            yield return RenderAndCompare("Assets/Samples/Niantic/hornedlizard.spz", "hornedlizard512_cpu", 60f, PoseInFrontOfBounds);
        }

        [UnityTest]
        public IEnumerator StudyRoom512()
        {
            yield return RenderAndCompare("Assets/Samples/InnerTest/study_room/study_room_500k.spz", "study_room512_cpu", 70f, PoseAtOrigin);
        }

        /// <summary>Same pose as RealScenePerformanceTests: slightly above the bounds center, looking down +Z.</summary>
        private static void PoseInFrontOfBounds(Camera camera, Bounds bounds)
        {
            camera.transform.position = bounds.center + new Vector3(0f, 0.3f, -bounds.size.z * 0.05f);
            camera.transform.rotation = Quaternion.identity;
        }

        /// <summary>InnerTest worlds are generated around the origin, which is the intended viewpoint.</summary>
        private static void PoseAtOrigin(Camera camera, Bounds bounds)
        {
            camera.transform.position = Vector3.zero;
            camera.transform.rotation = Quaternion.identity;
        }

        private static IEnumerator RenderAndCompare(string assetPath, string goldenName, float fieldOfView, System.Action<Camera, Bounds> pose)
        {
#if UNITY_EDITOR
            if (!File.Exists(assetPath)) Assert.Ignore("Sample file not present: " + assetPath);
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<GaussianSplatAsset>(assetPath);
            Assert.IsNotNull(asset, "asset at " + assetPath);

            using (new UrpTestPipeline())
            {
                var cameraObject = new GameObject("real golden camera");
                var splatObject = new GameObject("real golden splats");
                var target = new RenderTexture(Size, Size, 24, GraphicsFormat.R8G8B8A8_UNorm);
                Texture2D capture = null;
                try
                {
                    Camera camera = cameraObject.AddComponent<Camera>();
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = Color.black;
                    camera.fieldOfView = fieldOfView;
                    camera.nearClipPlane = 0.05f;
                    camera.farClipPlane = 1000f;
                    camera.targetTexture = target;
                    cameraObject.AddComponent<UniversalAdditionalCameraData>();

                    var renderer = splatObject.AddComponent<GaussianSplatRenderer>();
                    renderer.SetSorterKind(SplatSorterKind.Cpu);
                    renderer.SetData(asset.LoadData(), true);
                    pose(camera, renderer.WorldBounds);

                    while (!renderer.Gpu.IsFullyUploaded)
                    {
                        camera.Render();
                        yield return null;
                    }

                    // The CPU sorter is asynchronous: the frame after the last upload schedules the sort of the full
                    // chunk set, and the renderer keeps drawing the previous (partial) order until that job is done.
                    // Test frames are far shorter than the 4 ms job, so wait for it explicitly instead of counting frames.
                    camera.Render();
                    yield return null;
                    ((CpuCountingSorter)renderer.Sorter).CompleteNow();
                    camera.Render();
                    yield return null;

                    capture = GoldenImage.Capture(target);
                    GoldenImage.Assert(capture, goldenName);
                }
                finally
                {
                    if (capture != null) Object.DestroyImmediate(capture);
                    Object.DestroyImmediate(splatObject);
                    Object.DestroyImmediate(cameraObject);
                    target.Release();
                }
            }
#else
            Assert.Ignore("Editor only: the sample assets are loaded through the AssetDatabase.");
            yield break;
#endif
        }
    }
}
