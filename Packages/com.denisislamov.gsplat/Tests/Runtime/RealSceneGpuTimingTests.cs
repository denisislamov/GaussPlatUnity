using System.Collections;
using System.IO;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Profiling;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

#if UNITY_EDITOR
namespace GSplat.Tests
{
    /// <summary>
    /// Where the frame time goes: the "Gaussian Splats" profiler sampler wraps our sort and draw passes, so its GPU
    /// time is the renderer's own cost, separate from URP overhead. Variants shrink the splat count and drop the
    /// fragment work to see what the cost scales with.
    /// </summary>
    public sealed class RealSceneGpuTimingTests
    {
        private const string SamplePath = "Assets/Samples/Niantic/hornedlizard.spz";

        [UnityTest, Performance]
        public IEnumerator HornedLizardBreakdown([Values(0, 1, 2, 3, 4, 5)] int variant)
        {
            yield return Breakdown(variant);
        }

        /// <summary>Where the sub-pixel cull threshold should sit: cost and drawn count per threshold, plus a 4x-pixel target to separate fill from primitive count.</summary>
        [UnityTest, Performance]
        public IEnumerator HornedLizardCullThreshold([Values(0, 1, 2, 3, 4)] int variant)
        {
            float threshold = variant switch { 0 => 0.3f, 1 => 1.0f, 2 => 1.5f, 3 => 2.0f, _ => 1.5f };
            int width = variant == 4 ? 2160 : 1080;
            int height = variant == 4 ? 3840 : 1920;
            yield return MeasureVariant($"minPixelRadius {threshold} at {width}x{height}", 0, SplatSorterKind.Gpu, threshold, SplatDebugMode.None, width, height);
        }

        private IEnumerator Breakdown(int variant)
        {
            if (!File.Exists(SamplePath)) Assert.Ignore("Sample file not present: " + SamplePath);
            if (!GpuCountingSorter.IsSupported) Assert.Ignore("No compute shaders on this device.");

            int maxSplats = variant == 1 ? 200000 : variant == 2 ? 50000 : 0;
            SplatDebugMode debugMode = variant == 3 ? SplatDebugMode.Overdraw : SplatDebugMode.None;
            SplatSorterKind sorterKind = variant == 4 ? SplatSorterKind.Cpu : SplatSorterKind.Gpu;
            float minPixelRadius = variant == 5 ? 3f : 0.3f;
            string label = variant switch
            {
                0 => "full",
                1 => "200k subset",
                2 => "50k subset",
                3 => "full, overdraw debug (trivial fragment)",
                4 => "full, CPU sort (no compute on the GPU)",
                _ => "full, minPixelRadius 3 (small splats culled)"
            };

            yield return MeasureVariant(label, maxSplats, sorterKind, minPixelRadius, debugMode, 1080, 1920);
        }

        private static IEnumerator MeasureVariant(string label, int maxSplats, SplatSorterKind sorterKind, float minPixelRadius, SplatDebugMode debugMode, int width, int height)
        {
            byte[] bytes = File.ReadAllBytes(SamplePath);
            var options = new SplatImportOptions { SourceCoordinateSystem = SplatCoordinateSystem.Rub, TargetShDegree = 0, MaxSplatCount = maxSplats };
            GsplatData data = null;
            System.Exception failure = null;
            yield return SplatLoaderTests.Await(SplatLoader.BuildAsync(bytes, options), d => data = d, e => failure = e);
            Assert.IsNull(failure, failure?.ToString());

            Profiler.enabled = true;
            Recorder recorder = Recorder.Get("Gaussian Splats");
            recorder.enabled = true;
            recorder.CollectFromAllThreads(); // the pass runs on the render thread
            var frameTimings = new FrameTiming[1];

            using (new UrpTestPipeline())
            {
                var cameraObject = new GameObject("timing camera");
                var splatObject = new GameObject("timing splats");
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
                    renderer.ShDegree = 0;
                    renderer.MaxStdDev = 2.236f;
                    renderer.MinPixelRadius = minPixelRadius;
                    renderer.DebugMode = debugMode;
                    renderer.SetData(data, true);
                    Bounds bounds = renderer.WorldBounds;
                    camera.transform.position = bounds.center + new Vector3(0f, 0.3f, -bounds.size.z * 0.05f);

                    for (int frame = 0; frame < 10 || !renderer.Gpu.IsFullyUploaded; frame++)
                    {
                        camera.transform.Rotate(0f, 0.5f, 0f);
                        camera.Render();
                        yield return null;
                    }

                    var wall = new SampleGroup("WallMs", SampleUnit.Millisecond);
                    var gpu = new SampleGroup("PassGpuMs", SampleUnit.Millisecond);
                    var cpu = new SampleGroup("PassCpuMs", SampleUnit.Millisecond);
                    float wallTotal = 0f, gpuTotal = 0f, cpuTotal = 0f, frameGpuTotal = 0f;
                    int frameGpuSamples = 0;
                    const int frames = 30;
                    for (int frame = 0; frame < frames; frame++)
                    {
                        camera.transform.Rotate(0f, 0.5f, 0f);
                        float start = Time.realtimeSinceStartup;
                        camera.Render();
                        UnityEngine.Rendering.AsyncGPUReadback.Request(target).WaitForCompletion();
                        float wallMs = (Time.realtimeSinceStartup - start) * 1000f;
                        yield return null; // the recorder reports the previous frame's samples after the frame ends

                        FrameTimingManager.CaptureFrameTimings();
                        if (FrameTimingManager.GetLatestTimings(1, frameTimings) > 0 && frameTimings[0].gpuFrameTime > 0)
                        {
                            frameGpuTotal += (float)frameTimings[0].gpuFrameTime;
                            frameGpuSamples++;
                        }

                        float gpuMs = recorder.gpuElapsedNanoseconds / 1e6f;
                        float cpuMs = recorder.elapsedNanoseconds / 1e6f;
                        Measure.Custom(wall, wallMs);
                        Measure.Custom(gpu, gpuMs);
                        Measure.Custom(cpu, cpuMs);
                        wallTotal += wallMs;
                        gpuTotal += gpuMs;
                        cpuTotal += cpuMs;
                    }

                    string frameGpu = frameGpuSamples > 0 ? $"{frameGpuTotal / frameGpuSamples:F1} ms" : "n/a";
                    Debug.Log($"GSplat timing: {label}: drawn {renderer.LastDrawnSplatCount:N0}, wall {wallTotal / frames:F1} ms, pass GPU {gpuTotal / frames:F2} ms, pass CPU {cpuTotal / frames:F2} ms, whole-frame GPU {frameGpu} (recorder gpu blocks {recorder.gpuSampleBlockCount})");
                }
                finally
                {
                    recorder.enabled = false;
                    Object.DestroyImmediate(splatObject);
                    Object.DestroyImmediate(cameraObject);
                    target.Release();
                }
            }

        }
    }
}
#endif
