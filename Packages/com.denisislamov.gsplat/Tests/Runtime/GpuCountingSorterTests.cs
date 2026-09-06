using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace GSplat.Tests
{
    public sealed class GpuCountingSorterTests
    {
        [UnityTest]
        public IEnumerator GpuOrderIsBackToFront([Values(1, 5000, 70000, 300000)] int count, [Values(false, true)] bool radial)
        {
            if (!GpuCountingSorter.IsSupported) Assert.Ignore("No compute shaders on this device.");
            ComputeShader shader = GpuCountingSorter.LoadShader();
            Assert.IsNotNull(shader, "GSplatCountingSort.compute must be in Resources");

            using (var scene = new SortTestScene(count) { Radial = radial })
            using (var sorter = new GpuCountingSorter(shader, count))
            using (var commands = new CommandBuffer())
            {
                sorter.Sort(scene.Input(), true);
                Assert.AreEqual(count, sorter.OrderedSplatCount);
                sorter.RecordCompute(commands);
                Graphics.ExecuteCommandBuffer(commands);

                var order = new List<uint>();
                yield return SortTestScene.ReadOrder(sorter.OrderTexture, count, order);
                scene.AssertBackToFront(order);
            }
        }
    }

    public sealed class SorterOptionTests
    {
        /// <summary>P5/P6: the narrow key and the sliced CPU sort must produce the same back-to-front order as the defaults.</summary>
        [UnityTest]
        public IEnumerator NarrowKeysAndSlicingKeepTheOrderBackToFront([Values(12, 16)] int keyBits, [Values(false, true)] bool sliced)
        {
            const int count = 70000;
            var options = new SplatSorterOptions { Kind = SplatSorterKind.Cpu, KeyBits = keyBits, TimeSliced = sliced, SlotsPerFrame = 20000 };
            using (var scene = new SortTestScene(count) { Radial = true })
            using (var sorter = new CpuCountingSorter(count, options))
            {
                sorter.Sort(scene.Input(), true);
                int frames = 0;
                while (sorter.IsSorting && frames < 100)
                {
                    yield return null;
                    sorter.Sort(scene.Input(), false); // advances the slices / collects the job
                    frames++;
                }

                sorter.CompleteNow();
                Assert.AreEqual(count, sorter.OrderedSplatCount);
                if (sliced) Assert.Greater(frames, 3, "20k slots per frame over 131k slots must take several frames");

                var order = new List<uint>();
                yield return SortTestScene.ReadOrder(sorter.OrderTexture, count, order);
                scene.AssertBackToFront(order, keyBits);
            }
        }

        [UnityTest]
        public IEnumerator GpuNarrowKeysAreBackToFront()
        {
            if (!GpuCountingSorter.IsSupported) Assert.Ignore("No compute shaders on this device.");
            const int count = 70000;
            var options = new SplatSorterOptions { Kind = SplatSorterKind.Gpu, KeyBits = 12 };
            using (var scene = new SortTestScene(count) { Radial = true })
            using (var sorter = new GpuCountingSorter(GpuCountingSorter.LoadShader(), count, options))
            using (var commands = new CommandBuffer())
            {
                sorter.Sort(scene.Input(), true);
                sorter.RecordCompute(commands);
                Graphics.ExecuteCommandBuffer(commands);

                var order = new List<uint>();
                yield return SortTestScene.ReadOrder(sorter.OrderTexture, count, order);
                scene.AssertBackToFront(order, 12);
            }
        }

        /// <summary>P3: with a budget, a chunk contributes exactly its first k slots and nothing past them.</summary>
        [UnityTest]
        public IEnumerator ChunkBudgetLimitsEachChunkToItsPrefix()
        {
            const int count = SplatChunkInfo.Size + 10000; // two chunks
            using (var scene = new SortTestScene(count) { Radial = true })
            using (var sorter = new CpuCountingSorter(count))
            {
                SplatSortInput input = scene.Input();
                var budgets = new NativeArray<int>(2, Allocator.TempJob);
                budgets[0] = 1000;
                budgets[1] = 500;
                input.ChunkBudgets = budgets;
                sorter.Sort(input, true);
                budgets.Dispose(); // the sorter copies the budgets when the sort starts
                yield return null;
                sorter.CompleteNow();

                Assert.AreEqual(1500, sorter.OrderedSplatCount);
                var order = new List<uint>();
                yield return SortTestScene.ReadOrder(sorter.OrderTexture, 1500, order);
                foreach (uint splatIndex in order)
                {
                    int chunk = (int)(splatIndex / SplatChunkInfo.Size);
                    int local = (int)(splatIndex % SplatChunkInfo.Size);
                    Assert.Less(local, chunk == 0 ? 1000 : 500, "splat " + splatIndex);
                }
            }
        }
    }

    public sealed class CpuCountingSorterRuntimeTests
    {
        [UnityTest]
        public IEnumerator CpuOrderIsBackToFrontAfterCollecting([Values(1, 5000, 70000)] int count, [Values(false, true)] bool radial)
        {
            using (var scene = new SortTestScene(count) { Radial = radial })
            using (var sorter = new CpuCountingSorter(count))
            {
                sorter.Sort(scene.Input(), true);
                Assert.AreEqual(0, sorter.OrderedSplatCount, "the first result arrives a frame later");
                yield return null;
                sorter.CompleteNow();
                Assert.AreEqual(count, sorter.OrderedSplatCount);

                var order = new List<uint>();
                yield return SortTestScene.ReadOrder(sorter.OrderTexture, count, order);
                scene.AssertBackToFront(order);
            }
        }

        [UnityTest]
        public IEnumerator SecondPrepareWithoutResortKeepsTheOrder()
        {
            using (var scene = new SortTestScene(3000))
            using (var sorter = new CpuCountingSorter(3000))
            {
                sorter.Sort(scene.Input(), true);
                yield return null;
                sorter.Sort(scene.Input(), false); // collects the finished job, schedules nothing
                sorter.CompleteNow();
                Assert.AreEqual(3000, sorter.OrderedSplatCount);
                Assert.IsFalse(sorter.IsSorting);
            }
        }
    }
}
