using System.Collections;
using NUnit.Framework;
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
            using (var scene = new SortTestScene(SplatCount, 1))
            using (var sorter = new CpuCountingSorter(SplatCount))
            {
                SplatSortInput input = scene.Input();
                Measure.Method(() =>
                    {
                        sorter.Sort(input, true);
                        sorter.CompleteNow();
                    })
                    .WarmupCount(3)
                    .MeasurementCount(20)
                    .Run();
            }
        }

        [UnityTest, Performance]
        public IEnumerator GpuCountingSort500k()
        {
            if (!GpuCountingSorter.IsSupported) Assert.Ignore("No compute shaders on this device.");
            ComputeShader shader = GpuCountingSorter.LoadShader();

            using (var scene = new SortTestScene(SplatCount, 1))
            using (var sorter = new GpuCountingSorter(shader, SplatCount))
            using (var commands = new CommandBuffer())
            {
                SplatSortInput input = scene.Input();

                // Wall time around execute + a readback wait: includes the sync a frame that needs the result right away would pay.
                for (int iteration = 0; iteration < 3; iteration++)
                {
                    RunOnce(sorter, input, commands);
                }

                var sampleGroup = new SampleGroup("GpuSortWithSync", SampleUnit.Millisecond);
                for (int iteration = 0; iteration < 20; iteration++)
                {
                    float start = Time.realtimeSinceStartup;
                    RunOnce(sorter, input, commands);
                    Measure.Custom(sampleGroup, (Time.realtimeSinceStartup - start) * 1000f);
                    yield return null;
                }
            }
        }

        private static void RunOnce(GpuCountingSorter sorter, SplatSortInput input, CommandBuffer commands)
        {
            commands.Clear();
            sorter.Sort(input, true);
            sorter.RecordCompute(commands);
            Graphics.ExecuteCommandBuffer(commands);
            AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(sorter.OrderTexture, 0, TextureFormat.RGBA32);
            request.WaitForCompletion();
        }
    }
}
