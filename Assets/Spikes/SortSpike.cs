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
    /// player data (Application.persistentDataPath/BenchmarkResults). Uses random positions, or the real positions of
    /// a GaussianSplatAsset when one is assigned.
    /// </summary>
    public sealed class SortSpike : MonoBehaviour
    {
        [SerializeField, Tooltip("Optional: sort the positions of this asset instead of random points.")]
        private GaussianSplatAsset asset;

        [SerializeField, Tooltip("Splats to sort when no asset is assigned.")]
        private int randomSplatCount = 500000;

        [SerializeField, Range(3, 100)] private int iterations = 20;

        [SerializeField, Tooltip("Compute shader Runtime/Shaders/Resources/GSplatCountingSort.compute (loaded from Resources when empty).")]
        private ComputeShader countingSortShader;

        private readonly StringBuilder screenText = new StringBuilder();
        private readonly List<float> cpuMilliseconds = new List<float>();
        private readonly List<float> gpuMilliseconds = new List<float>();

        private NativeArray<float3> positions;
        private NativeArray<uint> cpuOrder;
        private CpuCountingSorter cpuSorter;
        private GpuCountingSorter gpuSorter;
        private GraphicsBuffer positionBuffer;
        private GraphicsBuffer orderBuffer;
        private CommandBuffer gpuCommands;
        private float minDepth;
        private float maxDepth;
        private int iterationsDone;
        private bool finished;

        private static readonly float3 CameraPosition = new float3(0f, 0f, -100f);
        private static readonly float3 CameraForward = new float3(0f, 0f, 1f);

        private void Start()
        {
            LoadPositions();
            cpuOrder = new NativeArray<uint>(positions.Length, Allocator.Persistent);
            cpuSorter = new CpuCountingSorter();

            if (countingSortShader == null) countingSortShader = Resources.Load<ComputeShader>("GSplatCountingSort");
            if (GpuCountingSorter.IsSupported && countingSortShader != null)
            {
                gpuSorter = new GpuCountingSorter(countingSortShader);
                var positions4 = new NativeArray<float4>(positions.Length, Allocator.Temp);
                minDepth = float.MaxValue;
                maxDepth = float.MinValue;
                for (int splatIndex = 0; splatIndex < positions.Length; splatIndex++)
                {
                    positions4[splatIndex] = new float4(positions[splatIndex], 0f);
                    float depth = SplatSortKeys.ViewDepth(positions[splatIndex], CameraPosition, CameraForward);
                    minDepth = math.min(minDepth, depth);
                    maxDepth = math.max(maxDepth, depth);
                }

                positionBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, positions.Length, 16);
                positionBuffer.SetData(positions4);
                positions4.Dispose();
                orderBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, positions.Length, 4);
                gpuCommands = new CommandBuffer { name = "GSplat sort spike" };
                gpuSorter.Record(gpuCommands, positionBuffer, positions.Length, orderBuffer, CameraPosition, CameraForward, minDepth, maxDepth);
            }

            screenText.AppendLine($"GSplat sort spike: {positions.Length:N0} splats, {SystemInfo.graphicsDeviceType}, {SystemInfo.deviceModel}");
            screenText.AppendLine($"Compute shaders: {(GpuCountingSorter.IsSupported ? "yes" : "no")}");
        }

        private void Update()
        {
            if (finished) return;

            // One iteration per frame so the screen keeps updating on slow phones.
            float cpuStart = Time.realtimeSinceStartup;
            cpuSorter.Sort(positions, CameraPosition, CameraForward, cpuOrder);
            cpuMilliseconds.Add((Time.realtimeSinceStartup - cpuStart) * 1000f);

            if (gpuSorter != null)
            {
                // Wall time around execute + synchronous readback: the cost of a frame that needs the order right away.
                float gpuStart = Time.realtimeSinceStartup;
                Graphics.ExecuteCommandBuffer(gpuCommands);
                AsyncGPUReadbackRequest readback = AsyncGPUReadback.Request(orderBuffer);
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

            string json = "{\n" +
                $"  \"device\": \"{SystemInfo.deviceModel}\",\n" +
                $"  \"graphicsApi\": \"{SystemInfo.graphicsDeviceType}\",\n" +
                $"  \"splatCount\": {positions.Length},\n" +
                $"  \"cpuSortMedianMs\": {Median(cpuMilliseconds).ToString("F3", CultureInfo.InvariantCulture)},\n" +
                $"  \"cpuSortP95Ms\": {Percentile(cpuMilliseconds, 0.95f).ToString("F3", CultureInfo.InvariantCulture)},\n" +
                $"  \"gpuSortMedianMs\": {(gpuMilliseconds.Count > 0 ? Median(gpuMilliseconds).ToString("F3", CultureInfo.InvariantCulture) : "null")},\n" +
                $"  \"gpuSortP95Ms\": {(gpuMilliseconds.Count > 0 ? Percentile(gpuMilliseconds, 0.95f).ToString("F3", CultureInfo.InvariantCulture) : "null")}\n" +
                "}\n";
            string folder = Path.Combine(Application.persistentDataPath, "BenchmarkResults");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"sort-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.WriteAllText(path, json);
            screenText.AppendLine("Report: " + path);
            Debug.Log(screenText.ToString());
        }

        private void LoadPositions()
        {
            if (asset != null)
            {
                using (GsplatData data = asset.LoadData())
                {
                    positions = new NativeArray<float3>(data.SplatCount, Allocator.Persistent);
                    for (int splatIndex = 0; splatIndex < data.SplatCount; splatIndex++)
                    {
                        PackedSplat.Unpack(data.Packed[splatIndex], out float3 relative, out _, out _, out _, out _);
                        positions[splatIndex] = relative + data.Chunks[splatIndex / SplatChunkInfo.Size].Center;
                    }
                }

                return;
            }

            var random = new Unity.Mathematics.Random(1);
            positions = new NativeArray<float3>(randomSplatCount, Allocator.Persistent);
            for (int splatIndex = 0; splatIndex < randomSplatCount; splatIndex++) positions[splatIndex] = random.NextFloat3(-50f, 50f);
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
            if (positions.IsCreated) positions.Dispose();
            if (cpuOrder.IsCreated) cpuOrder.Dispose();
            cpuSorter?.Dispose();
            gpuSorter?.Dispose();
            positionBuffer?.Dispose();
            orderBuffer?.Dispose();
            gpuCommands?.Dispose();
        }
    }
}
