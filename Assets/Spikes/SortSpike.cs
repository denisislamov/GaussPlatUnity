using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace GSplat.Sandbox
{
    /// <summary>
    /// E0-T3 spike: times the CPU (Burst) and GPU (compute) counting sorts on this device and shows the numbers on
    /// screen, so the same build can be run on every reference phone. Also writes a JSON report next to the
    /// player data (Application.persistentDataPath/BenchmarkResults). Sorts a random scene, or the assigned
    /// GaussianSplatAsset when one is set.
    /// </summary>
    public sealed class SortSpike : MonoBehaviour
    {
        [SerializeField, Tooltip("Optional: sort this asset instead of random splats.")]
        private GaussianSplatAsset asset;

        [SerializeField, Tooltip("Splats to sort when no asset is assigned.")]
        private int randomSplatCount = 500000;

        [SerializeField, Range(3, 100)] private int iterations = 20;

        private readonly StringBuilder screenText = new StringBuilder();
        private readonly List<float> cpuMilliseconds = new List<float>();
        private readonly List<float> gpuMilliseconds = new List<float>();

        private GsplatData data;
        private SplatGpuData gpu;
        private NativeArray<int> visibleChunks;
        private GraphicsBuffer visibleChunkBuffer;
        private CpuCountingSorter cpuSorter;
        private GpuCountingSorter gpuSorter;
        private CommandBuffer gpuCommands;
        private SplatSortInput input;
        private int iterationsDone;
        private bool finished;

        private static readonly float3 CameraPosition = new float3(0f, 0f, -100f);
        private static readonly float3 CameraForward = new float3(0f, 0f, 1f);

        private void Start()
        {
            LoadData();
            gpu = new SplatGpuData(data);
            gpu.UploadAll();

            visibleChunks = new NativeArray<int>(data.ChunkCount, Allocator.Persistent);
            for (int chunkIndex = 0; chunkIndex < data.ChunkCount; chunkIndex++) visibleChunks[chunkIndex] = chunkIndex;
            visibleChunkBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, data.ChunkCount, sizeof(int));
            visibleChunkBuffer.SetData(visibleChunks);

            SplatSortKeys.DepthRange(data.Chunks, visibleChunks, CameraPosition, CameraForward, out float minDepth, out float maxDepth);
            input = new SplatSortInput
            {
                Data = data,
                Gpu = gpu,
                VisibleChunks = visibleChunks,
                VisibleChunkBuffer = visibleChunkBuffer,
                VisibleSplatCount = data.SplatCount,
                CameraPositionLocal = CameraPosition,
                CameraForwardLocal = CameraForward,
                MinDepth = minDepth,
                MaxDepth = maxDepth
            };

            cpuSorter = new CpuCountingSorter(data.SplatCount);
            ComputeShader shader = GpuCountingSorter.LoadShader();
            if (shader != null)
            {
                gpuSorter = new GpuCountingSorter(shader, data.SplatCount);
                gpuCommands = new CommandBuffer { name = "GSplat sort spike" };
            }

            screenText.AppendLine($"GSplat sort spike: {data.SplatCount:N0} splats, {SystemInfo.graphicsDeviceType}, {SystemInfo.deviceModel}");
            screenText.AppendLine($"Compute shaders: {(GpuCountingSorter.IsSupported ? "yes" : "no")}");
        }

        private void Update()
        {
            if (finished) return;

            // One iteration per frame so the screen keeps updating on slow phones.
            float cpuStart = Time.realtimeSinceStartup;
            cpuSorter.PrepareOnMainThread(input, true);
            cpuSorter.CompleteNow();
            cpuMilliseconds.Add((Time.realtimeSinceStartup - cpuStart) * 1000f);

            if (gpuSorter != null)
            {
                // Wall time around execute + synchronous readback: the cost of a frame that needs the order right away.
                float gpuStart = Time.realtimeSinceStartup;
                gpuCommands.Clear();
                gpuSorter.PrepareOnMainThread(input, true);
                gpuSorter.RecordCompute(gpuCommands);
                Graphics.ExecuteCommandBuffer(gpuCommands);
                AsyncGPUReadbackRequest readback = AsyncGPUReadback.Request(gpuSorter.OrderTexture, 0, TextureFormat.RGBA32);
                readback.WaitForCompletion();
                gpuMilliseconds.Add((Time.realtimeSinceStartup - gpuStart) * 1000f);
            }

            iterationsDone++;
            if (iterationsDone >= iterations + 3) // the first 3 are warm-up
            {
                finished = true;
                Report();
            }
        }

        private void Report()
        {
            cpuMilliseconds.RemoveRange(0, 3);
            if (gpuMilliseconds.Count > 3) gpuMilliseconds.RemoveRange(0, 3);

            screenText.AppendLine($"CPU Burst counting sort: median {Median(cpuMilliseconds):F2} ms, p95 {Percentile(cpuMilliseconds, 0.95f):F2} ms");
            if (gpuMilliseconds.Count > 0)
            {
                screenText.AppendLine($"GPU counting sort (+sync readback): median {Median(gpuMilliseconds):F2} ms, p95 {Percentile(gpuMilliseconds, 0.95f):F2} ms");
            }

            string gpuMedian = gpuMilliseconds.Count > 0 ? Median(gpuMilliseconds).ToString("F3", CultureInfo.InvariantCulture) : "null";
            string gpuP95 = gpuMilliseconds.Count > 0 ? Percentile(gpuMilliseconds, 0.95f).ToString("F3", CultureInfo.InvariantCulture) : "null";
            string json = "{\n" +
                $"  \"device\": \"{SystemInfo.deviceModel}\",\n" +
                $"  \"graphicsApi\": \"{SystemInfo.graphicsDeviceType}\",\n" +
                $"  \"splatCount\": {data.SplatCount},\n" +
                $"  \"cpuSortMedianMs\": {Median(cpuMilliseconds).ToString("F3", CultureInfo.InvariantCulture)},\n" +
                $"  \"cpuSortP95Ms\": {Percentile(cpuMilliseconds, 0.95f).ToString("F3", CultureInfo.InvariantCulture)},\n" +
                $"  \"gpuSortMedianMs\": {gpuMedian},\n" +
                $"  \"gpuSortP95Ms\": {gpuP95}\n" +
                "}\n";
            string folder = Path.Combine(Application.persistentDataPath, "BenchmarkResults");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"sort-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.WriteAllText(path, json);
            screenText.AppendLine("Report: " + path);
            Debug.Log(screenText.ToString());
        }

        private void LoadData()
        {
            if (asset != null)
            {
                data = asset.LoadData();
                return;
            }

            using (SplatCloud cloud = RandomCloud(randomSplatCount))
            {
                var options = new SplatImportOptions { SourceCoordinateSystem = SplatCoordinateSystem.Ruf, PruneAlphaBelow = 0f };
                data = GsplatBuilder.Build(cloud, options);
            }
        }

        private static SplatCloud RandomCloud(int count)
        {
            var random = new Unity.Mathematics.Random(1);
            var cloud = new SplatCloud(count, 0, false);
            for (int splatIndex = 0; splatIndex < count; splatIndex++)
            {
                cloud.Positions[splatIndex] = random.NextFloat3(-50f, 50f);
                cloud.LogScales[splatIndex] = new float3(-4f);
                cloud.Rotations[splatIndex] = new float4(0f, 0f, 0f, 1f);
                cloud.Alphas[splatIndex] = 1f;
                cloud.Colors[splatIndex] = float3.zero;
            }

            return cloud;
        }

        private static float Median(List<float> values)
        {
            return Percentile(values, 0.5f);
        }

        private static float Percentile(List<float> values, float fraction)
        {
            var sorted = new List<float>(values);
            sorted.Sort();
            int index = Mathf.Clamp(Mathf.RoundToInt((sorted.Count - 1) * fraction), 0, sorted.Count - 1);
            return sorted[index];
        }

        private void OnGUI()
        {
            GUI.skin.label.fontSize = Mathf.Max(14, Screen.height / 40);
            GUILayout.BeginArea(new Rect(20f, 20f, Screen.width - 40f, Screen.height - 40f));
            GUILayout.Label(finished ? screenText.ToString() : screenText + $"\nRunning... {iterationsDone}/{iterations + 3}");
            GUILayout.EndArea();
        }

        private void OnDestroy()
        {
            cpuSorter?.Dispose();
            gpuSorter?.Dispose();
            gpuCommands?.Dispose();
            visibleChunkBuffer?.Dispose();
            if (visibleChunks.IsCreated) visibleChunks.Dispose();
            gpu?.Dispose();
            data?.Dispose();
        }
    }
}
