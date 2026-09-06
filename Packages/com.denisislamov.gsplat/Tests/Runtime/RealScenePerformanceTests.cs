using System.Collections;
using System.IO;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace GSplat.Tests
{
    /// <summary>
    /// Frame time of a real capture at a phone-shaped 1080x1920 target, camera inside the scene (the way a viewer
    /// looks at it). Numbers go to the Performance Test Report; the log line gives the summary.
    /// </summary>
    public sealed class RealScenePerformanceTests
    {
        private const string SamplePath = "Assets/Samples/Niantic/hornedlizard.spz";

        [UnityTest, Performance]
        public IEnumerator HornedLizardPortrait1080x1920([Values(SplatSorterKind.Gpu, SplatSorterKind.Cpu)] SplatSorterKind sorterKind)
        {
            yield return Run(sorterKind, ShMath.MaxDegree, GaussianSplatRenderer.DefaultMaxStdDev, 0.5f, "sh3 sqrt8 (component defaults)");
        }

        /// <summary>Both samples with the component defaults: the number the samples scene shows on this machine.</summary>
        [UnityTest, Performance]
        public IEnumerator DefaultsPortrait1080x1920([Values("hornedlizard", "racoonfamily")] string sample)
        {
            yield return Run(SplatSorterKind.Gpu, ShMath.MaxDegree, GaussianSplatRenderer.DefaultMaxStdDev, 0.5f, "component defaults", 1080, 1920, true, sample);
        }

        /// <summary>Which knob buys the most: SH off, smaller quads (sqrt5), stricter sub-pixel cull.</summary>
        [UnityTest, Performance]
        public IEnumerator HornedLizardQualityKnobs([Values(0, 1, 2, 3, 4)] int variant)
        {
            switch (variant)
            {
                case 0: yield return Run(SplatSorterKind.Gpu, 0, GaussianSplatRenderer.DefaultMaxStdDev, 0.3f, "sh0 sqrt8"); break;
                case 1: yield return Run(SplatSorterKind.Gpu, 0, 2.236f, 0.3f, "sh0 sqrt5"); break;
                case 2: yield return Run(SplatSorterKind.Gpu, 0, 2.236f, 1.0f, "sh0 sqrt5 minPixel1"); break;
                case 3: yield return Run(SplatSorterKind.Gpu, 0, 2.236f, 0.3f, "sh0 sqrt5 540x960", 540, 960); break;
                default: yield return Run(SplatSorterKind.Gpu, 0, 2.236f, 0.3f, "EMPTY baseline (renderer off)", 1080, 1920, false); break;
            }
        }

        private static IEnumerator Run(SplatSorterKind sorterKind, int shDegree, float maxStdDev, float minPixelRadius, string label, int width = 1080, int height = 1920, bool drawSplats = true, string sample = "hornedlizard")
        {
#if UNITY_EDITOR
            string samplePath = $"Assets/Samples/Niantic/{sample}.spz";
            if (!File.Exists(samplePath)) Assert.Ignore("Sample file not present: " + samplePath);
            if (sorterKind == SplatSorterKind.Gpu && !GpuCountingSorter.IsSupported) Assert.Ignore("No compute shaders on this device.");
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<GaussianSplatAsset>(samplePath);
            Assert.IsNotNull(asset);

            using (new UrpTestPipeline())
            {
                var cameraObject = new GameObject("perf camera");
                var splatObject = new GameObject("perf splats");
                var target = new RenderTexture(width, height, 24, GraphicsFormat.R8G8B8A8_UNorm);
                try
                {
                    Camera camera = cameraObject.AddComponent<Camera>();
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = Color.black;
                    camera.targetTexture = target;
                    camera.farClipPlane = 1000f;
                    cameraObject.AddComponent<UniversalAdditionalCameraData>();

                    var renderer = splatObject.AddComponent<GaussianSplatRenderer>();
                    renderer.SetSorterKind(sorterKind);
                    renderer.ShDegree = shDegree;
                    renderer.MaxStdDev = maxStdDev;
                    renderer.MinPixelRadius = minPixelRadius;
                    renderer.SetData(asset.LoadData(), true);
                    Bounds bounds = renderer.WorldBounds;
                    camera.transform.position = bounds.center + new Vector3(0f, 0.3f, -bounds.size.z * 0.05f);
                    camera.transform.rotation = Quaternion.identity;

                    while (!renderer.Gpu.IsFullyUploaded)
                    {
                        camera.Render();
                        yield return null;
                    }

                    renderer.enabled = drawSplats;

                    for (int frame = 0; frame < 5; frame++)
                    {
                        // Small camera motion so the sort policy does not skip work.
                        camera.transform.Rotate(0f, 0.5f, 0f);
                        camera.Render();
                        yield return null;
                    }

                    var group = new SampleGroup("FrameMs", SampleUnit.Millisecond);
                    float total = 0f;
                    const int frames = 30;
                    for (int frame = 0; frame < frames; frame++)
                    {
                        camera.transform.Rotate(0f, 0.5f, 0f);
                        float start = Time.realtimeSinceStartup;
                        camera.Render();
                        // camera.Render() is synchronous on the CPU side but the GPU may lag; a readback forces completion.
                        UnityEngine.Rendering.AsyncGPUReadback.Request(target).WaitForCompletion();
                        float ms = (Time.realtimeSinceStartup - start) * 1000f;
                        Measure.Custom(group, ms);
                        total += ms;
                        yield return null;
                    }

                    Debug.Log($"GSplat perf: {sample} {renderer.LastDrawnSplatCount:N0}/{renderer.SplatCount:N0} splats drawn, {renderer.LastVisibleChunkCount} chunks, sort {sorterKind}, {label}, {width}x{height}: {total / frames:F1} ms per frame (CPU submit + GPU).");
                }
                finally
                {
                    Object.DestroyImmediate(splatObject);
                    Object.DestroyImmediate(cameraObject);
                    target.Release();
                }
            }
#else
            Assert.Ignore("Editor only.");
            yield break;
#endif
        }
    }
}
