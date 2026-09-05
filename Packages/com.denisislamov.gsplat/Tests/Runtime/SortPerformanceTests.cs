using System.Collections;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using Unity.PerformanceTesting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace GSplat.Tests
{
    /// <summary>
    /// E0-T3 spike numbers. Run on each reference device (Test Runner -> Run on device) and read the medians from
    /// the performance report; the sandbox scene SortSpike shows the same numbers on screen.
    /// </summary>
    public sealed class SortPerformanceTests
    {
        private const int SplatCount = 500000;

        [Test, Performance]
        public void CpuCountingSort500k()
        {
            var random = new Unity.Mathematics.Random(1);
            var positions = new NativeArray<float3>(SplatCount, Allocator.Persistent);
            var order = new NativeArray<uint>(SplatCount, Allocator.Persistent);
            for (int splatIndex = 0; splatIndex < SplatCount; splatIndex++) positions[splatIndex] = random.NextFloat3(-50f, 50f);

            using (var sorter = new CpuCountingSorter())
            {
                Measure.Method(() => sorter.Sort(positions, new float3(0f, 0f, -100f), new float3(0f, 0f, 1f), order))
                    .WarmupCount(3)
                    .MeasurementCount(20)
                    .Run();
            }

            positions.Dispose();
            order.Dispose();
        }

        [UnityTest, Performance]
        public IEnumerator GpuCountingSort500k()
        {
            if (!GpuCountingSorter.IsSupported) Assert.Ignore("No compute shaders on this device.");
            var shader = Resources.Load<ComputeShader>("GSplatCountingSort");
            if (shader == null) Assert.Ignore("GSplatCountingSort.compute is not in a Resources folder.");

            var random = new Unity.Mathematics.Random(1);
            var positions = new NativeArray<float4>(SplatCount, Allocator.Persistent);
            for (int splatIndex = 0; splatIndex < SplatCount; splatIndex++) positions[splatIndex] = new float4(random.NextFloat3(-50f, 50f), 0f);
            var positionBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, SplatCount, 16);
            var orderBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, SplatCount, 4);
            positionBuffer.SetData(positions);

            using (var sorter = new GpuCountingSorter(shader))
            using (var commands = new CommandBuffer())
            {
                sorter.Record(commands, positionBuffer, SplatCount, orderBuffer, new float3(0f, 0f, -100f), new float3(0f, 0f, 1f), 0f, 200f);
                // GPU time is measured as wall time around execute + a readback wait: it includes the sync, which is
                // exactly what a frame that needs the result immediately would pay.
                for (int iteration = 0; iteration < 3; iteration++)
                {
                    Graphics.ExecuteCommandBuffer(commands);
                    AsyncGPUReadbackRequest warmup = AsyncGPUReadback.Request(orderBuffer);
                    warmup.WaitForCompletion();
                }

                var sampleGroup = new SampleGroup("GpuSortWithSync", SampleUnit.Millisecond);
                for (int iteration = 0; iteration < 20; iteration++)
                {
                    float start = Time.realtimeSinceStartup;
                    Graphics.ExecuteCommandBuffer(commands);
                    AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(orderBuffer);
                    request.WaitForCompletion();
                    Measure.Custom(sampleGroup, (Time.realtimeSinceStartup - start) * 1000f);
                    yield return null;
                }
            }

            positionBuffer.Dispose();
            orderBuffer.Dispose();
            positions.Dispose();
        }
    }
}
