using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace GSplat
{
    /// <summary>
    /// P1: the measurement everything else is judged by. Plays a fixed camera motion for a fixed time under one or
    /// more settings variants, records frame times and the renderer counters, and writes one JSON report to
    /// <see cref="Application.persistentDataPath"/> (and to the log, which is how it leaves a web page). Started from
    /// the debug menu or from the page (?bench=matrix). While it runs the fly camera is off and the settings are the
    /// variant's; the user's settings come back afterwards.
    /// </summary>
    public static class SplatBenchmark
    {
        public const float WarmupSeconds = 1f;
        public const float MeasureSeconds = 20f;

        /// <summary>One settings variant: what to change on top of the user's current settings.</summary>
        public sealed class Variant
        {
            public readonly string Name;
            public readonly Action<SplatDebugSettings> Apply;

            public Variant(string name, Action<SplatDebugSettings> apply)
            {
                Name = name;
                Apply = apply;
            }
        }

        [Serializable]
        public sealed class VariantResult
        {
            public string name;
            public string settings;
            public int frames;
            public float meanMs;
            public float p50Ms;
            public float p95Ms;
            public float fps;
            public float drawnSplats;
            public float totalSplats;
            public float cpuSortMs;
            public float gpuFrameMs;
            public long managedMb;
            public long totalMb;
        }

        [Serializable]
        public sealed class Report
        {
            public string version;
            public string device;
            public string gpu;
            public string api;
            public string scene;
            public int screenWidth;
            public int screenHeight;
            public float dpi;
            public string startedAt;
            public List<VariantResult> variants = new List<VariantResult>();
        }

        /// <summary>What the debug menu shows: idle, or the variant in progress, or where the report went.</summary>
        public static string Status { get; private set; } = "";
        public static bool IsRunning { get; private set; }

        /// <summary>The settings as they are now, measured once.</summary>
        public static List<Variant> SingleVariant()
        {
            return new List<Variant> { new Variant("current", s => { }) };
        }

        /// <summary>The user's settings, then one knob changed at a time: the marginal cost of each knob on this device.</summary>
        public static List<Variant> KnobMatrix()
        {
            return new List<Variant>
            {
                new Variant("baseline", s => { }),
                new Variant("minPixelRadius 0.5", s => s.MinPixelRadius = 0.5f),
                new Variant("minPixelRadius 1.0", s => s.MinPixelRadius = 1f),
                new Variant("minPixelRadius 1.5", s => s.MinPixelRadius = 1.5f),
                new Variant("minPixelRadius 2.0", s => s.MinPixelRadius = 2f),
                new Variant("renderScale 0.85", s => s.RenderScale = 0.85f),
                new Variant("renderScale 0.7", s => s.RenderScale = 0.7f),
                new Variant("reach sqrt(5)", s => s.MaxStdDev = 2.236f),
                new Variant("triangle (3 vertices)", s => s.VerticesPerSplat = 3),
                new Variant("sorter GPU", s => s.SorterKind = SplatSorterKind.Gpu),
                new Variant("sorter CPU", s => s.SorterKind = SplatSorterKind.Cpu),
                new Variant("keyBits 12", s => s.SortKeyBits = 12),
                new Variant("cheap Gaussian", s => s.CheapGaussian = true),
                new Variant("no alpha clip", s => s.ClipLowAlpha = false),
                new Variant("chunk budget 1.0", s => s.ChunkBudgetSplatsPerPixel = 1f),
                new Variant("chunk budget 0.5", s => s.ChunkBudgetSplatsPerPixel = 0.5f)
            };
        }

        public static void Run(List<Variant> variants)
        {
            if (IsRunning) return;
            var host = new GameObject("GSplat Benchmark");
            host.AddComponent<SplatBenchmarkRunner>().Begin(variants);
        }

        internal static void SetStatus(string status, bool running)
        {
            Status = status;
            IsRunning = running;
        }
    }

    /// <summary>Drives the camera and collects the numbers for <see cref="SplatBenchmark"/>; destroys itself when the report is written.</summary>
    [AddComponentMenu("")]
    public sealed class SplatBenchmarkRunner : MonoBehaviour
    {
        private const float WarmupSeconds = SplatBenchmark.WarmupSeconds;
        private const float MeasureSeconds = SplatBenchmark.MeasureSeconds;

            private List<SplatBenchmark.Variant> variants;

            public void Begin(List<SplatBenchmark.Variant> toRun)
            {
                variants = toRun;
                StartCoroutine(RunAll());
            }

            private IEnumerator RunAll()
            {
                SplatBenchmark.SetStatus("starting", true);
                string userSettings = JsonUtility.ToJson(SplatDebugSettings.Current);
                Camera camera = Camera.main;
                SplatFlyCamera fly = camera != null ? camera.GetComponent<SplatFlyCamera>() : null;
                if (fly != null) fly.enabled = false;
                var controller = FindFirstObjectByType<SplatQualityController>();
                bool controllerWasEnabled = controller != null && controller.enabled;
                if (controller != null) controller.enabled = false;

                var loader = FindFirstObjectByType<WorldLoader>();
                while (loader != null && loader.State != WorldLoadState.Ready && loader.State != WorldLoadState.Failed && loader.State != WorldLoadState.Idle) yield return null;

                var report = new SplatBenchmark.Report
                {
                    version = GSplatVersion.Current,
                    device = SystemInfo.deviceModel,
                    gpu = SystemInfo.graphicsDeviceName,
                    api = SystemInfo.graphicsDeviceType.ToString(),
                    scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                    screenWidth = Screen.width,
                    screenHeight = Screen.height,
                    dpi = Screen.dpi,
                    startedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                };

                Vector3 startPosition = camera != null ? camera.transform.position : Vector3.zero;
                Quaternion startRotation = camera != null ? camera.transform.rotation : Quaternion.identity;
                try
                {
                    for (int variantIndex = 0; variantIndex < variants.Count; variantIndex++)
                    {
                        SplatBenchmark.Variant variant = variants[variantIndex];
                        SplatBenchmark.SetStatus($"{variantIndex + 1}/{variants.Count}: {variant.Name}", true);
                        JsonUtility.FromJsonOverwrite(userSettings, SplatDebugSettings.Current);
                        SplatDebugSettings.Current.QualityControllerEnabled = false;
                        variant.Apply(SplatDebugSettings.Current);
                        yield return Measure(variant, camera, startPosition, startRotation, report);
                    }
                }
                finally
                {
                    JsonUtility.FromJsonOverwrite(userSettings, SplatDebugSettings.Current);
                    SplatDebugSettings.NotifyChanged();
                    if (camera != null)
                    {
                        camera.transform.SetPositionAndRotation(startPosition, startRotation);
                    }

                    if (fly != null) fly.enabled = true;
                    if (controller != null) controller.enabled = controllerWasEnabled;
                    SplatBenchmark.SetStatus("finishing", false);
                }

                SplatBenchmark.SetStatus(WriteReport(report), false);
                Destroy(gameObject);
            }

            private IEnumerator Measure(SplatBenchmark.Variant variant, Camera camera, Vector3 startPosition, Quaternion startRotation, SplatBenchmark.Report report)
            {
                var frameTimes = new List<float>(2048);
                float drawnSum = 0f;
                float totalSum = 0f;
                float cpuSortSum = 0f;
                int cpuSortSamples = 0;
                float gpuSum = 0f;
                int gpuSamples = 0;
                float elapsed = -WarmupSeconds;

                while (elapsed < MeasureSeconds)
                {
                    float dt = Time.unscaledDeltaTime;
                    elapsed += dt;
                    MoveCamera(camera, startPosition, startRotation, Mathf.Max(0f, elapsed));
                    if (elapsed >= 0f)
                    {
                        frameTimes.Add(dt * 1000f);
                        foreach (GaussianSplatRenderer renderer in GaussianSplatRenderer.Active)
                        {
                            drawnSum += renderer.LastDrawnSplatCount;
                            totalSum += renderer.SplatCount;
                            if (renderer.Sorter is CpuCountingSorter cpu)
                            {
                                cpuSortSum += cpu.LastSortMilliseconds;
                                cpuSortSamples++;
                            }
                        }

                        FrameTimingManager.CaptureFrameTimings();
                        var timings = new FrameTiming[1];
                        if (FrameTimingManager.GetLatestTimings(1, timings) == 1 && timings[0].gpuFrameTime > 0)
                        {
                            gpuSum += (float)timings[0].gpuFrameTime;
                            gpuSamples++;
                        }
                    }

                    yield return null;
                }

                frameTimes.Sort();
                int frames = frameTimes.Count;
                float sum = 0f;
                foreach (float ms in frameTimes) sum += ms;
                var result = new SplatBenchmark.VariantResult
                {
                    name = variant.Name,
                    settings = SplatDebugSettings.Current.Describe(),
                    frames = frames,
                    meanMs = frames > 0 ? sum / frames : 0f,
                    p50Ms = frames > 0 ? frameTimes[frames / 2] : 0f,
                    p95Ms = frames > 0 ? frameTimes[Mathf.Clamp(Mathf.RoundToInt((frames - 1) * 0.95f), 0, frames - 1)] : 0f,
                    fps = sum > 0f ? frames * 1000f / sum : 0f,
                    drawnSplats = frames > 0 ? drawnSum / frames : 0f,
                    totalSplats = frames > 0 ? totalSum / frames : 0f,
                    cpuSortMs = cpuSortSamples > 0 ? cpuSortSum / cpuSortSamples : 0f,
                    gpuFrameMs = gpuSamples > 0 ? gpuSum / gpuSamples : 0f,
                    managedMb = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / (1024 * 1024),
                    totalMb = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024)
                };
                report.variants.Add(result);
                Debug.Log($"GSplat benchmark: {variant.Name}: {result.fps:F1} FPS, mean {result.meanMs:F1} ms, p95 {result.p95Ms:F1} ms, drawn {result.drawnSplats:N0}, cpu sort {result.cpuSortMs:F1} ms, gpu {result.gpuFrameMs:F1} ms");
            }

            /// <summary>A slow look around with a little walking: yaw +-45 degrees, pitch +-10, half a meter back and forth. Same path for every variant and device.</summary>
            private static void MoveCamera(Camera camera, Vector3 startPosition, Quaternion startRotation, float time)
            {
                if (camera == null) return;
                float yaw = 45f * Mathf.Sin(time * 2f * Mathf.PI / 12f);
                float pitch = 10f * Mathf.Sin(time * 2f * Mathf.PI / 7f);
                Quaternion rotation = startRotation * Quaternion.Euler(pitch, yaw, 0f);
                Vector3 position = startPosition + startRotation * Vector3.forward * (0.5f * Mathf.Sin(time * 2f * Mathf.PI / 9f));
                camera.transform.SetPositionAndRotation(position, rotation);
            }

            private static string WriteReport(SplatBenchmark.Report report)
            {
                string json = JsonUtility.ToJson(report, true);
                Debug.Log("GSplat benchmark report:\n" + json);
                if (Application.platform == RuntimePlatform.WebGLPlayer) return "Report written to the browser console.";

                try
                {
                    string path = Path.Combine(Application.persistentDataPath, $"gsplat-benchmark-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                    File.WriteAllText(path, json, new UTF8Encoding(false));
                    return "Report: " + path;
                }
                catch (Exception e)
                {
                    return "Report could not be written: " + e.Message;
                }
            }
    }
}
