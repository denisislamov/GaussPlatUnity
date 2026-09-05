using System.Collections;
using System.IO;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Profiling;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

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
        public IEnumerator HornedLizardBreakdown([Values(0, 1, 2, 3)] int variant)
        {
#if UNITY_EDITOR
            if (!File.Exists(SamplePath)) Assert.Ignore("Sample file not present: " + SamplePath);
            if (!GpuCountingSorter.IsSupported) Assert.Ignore("No compute shaders on this device.");

            int maxSplats = variant == 1 ? 200000 : variant == 2 ? 50000 : 0;
            SplatDebugMode debugMode = variant == 3 ? SplatDebugMode.Overdraw : SplatDebugMode.None;
            string label = variant == 0 ? "full" : variant == 1 ? "200k subset" : variant == 2 ? "50k subset" : "full, overdraw debug (trivial fragment)";

            byte[] bytes = File.ReadAllBytes(SamplePath);
            var options = new SplatImportOptions { SourceCoordinateSystem = SplatCoordinateSystem.Rub, TargetShDegree = 0, MaxSplatCount = maxSplats };
            GsplatData data = null;
            System.Exception failure = null;
            yield return SplatLoaderTests.Await(SplatLoader.BuildAsync(bytes, options), d => data = d, e => failure = e);
            Assert.IsNull(failure, failure?.ToString());

            Profiler.enabled = true;
            Recorder recorder = Recorder.Get("Gaussian Splats");
            recorder.enabled = true;

            using (new UrpTestPipeline())
            {
                var cameraObject = new GameObject("timing camera");
                var splatObject = new GameObject("timing splats");
                var target = new RenderTexture(1080, 1920, 24, GraphicsFormat.R8G8B8A8_UNorm);
                try
                {
                    Camera camera = cameraObject.AddComponent<Camera>();
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = Color.black;
                    camera.targetTexture = target;
                    camera.farClipPlane = 1000f;
                    cameraObject.AddComponent<UniversalAdditionalCameraData>();

                    var renderer = splatObject.AddComponent<GaussianSplatRenderer>();
                    renderer.SetSorterKind(SplatSorterKind.Gpu);
                    renderer.ShDegree = 0;
                    renderer.MaxStdDev = 2.236f;
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
                    float wallTotal = 0f, gpuTotal = 0f, cpuTotal = 0f;
                    const int frames = 30;
                    for (int frame = 0; frame < frames; frame++)
                    {
                        camera.transform.Rotate(0f, 0.5f, 0f);
                        float start = Time.realtimeSinceStartup;
                        camera.Render();
                        UnityEngine.Rendering.AsyncGPUReadback.Request(target).WaitForCompletion();
                        float wallMs = (Time.realtimeSinceStartup - start) * 1000f;
                        yield return null; // the recorder reports the previous frame's samples after the frame ends

                        float gpuMs = recorder.gpuElapsedNanoseconds / 1e6f;
                        float cpuMs = recorder.elapsedNanoseconds / 1e6f;
                        Measure.Custom(wall, wallMs);
                        Measure.Custom(gpu, gpuMs);
                        Measure.Custom(cpu, cpuMs);
                        wallTotal += wallMs;
                        gpuTotal += gpuMs;
                        cpuTotal += cpuMs;
                    }

                    Debug.Log($"GSplat timing: {label}: drawn {renderer.LastDrawnSplatCount:N0}, wall {wallTotal / frames:F1} ms, pass GPU {gpuTotal / frames:F2} ms, pass CPU {cpuTotal / frames:F2} ms (gpu sample count {recorder.gpuSampleBlockCount})");
                }
                finally
                {
                    recorder.enabled = false;
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
