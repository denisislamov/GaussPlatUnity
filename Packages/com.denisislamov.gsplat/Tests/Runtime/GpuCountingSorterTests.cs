using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace GSplat.Tests
{
    public sealed class GpuCountingSorterTests
    {
        [UnityTest]
        public IEnumerator GpuOrderIsBackToFront([Values(1, 5000, 70000, 300000)] int count)
        {
            if (!GpuCountingSorter.IsSupported) Assert.Ignore("No compute shaders on this device.");
            ComputeShader shader = GpuCountingSorter.LoadShader();
            Assert.IsNotNull(shader, "GSplatCountingSort.compute must be in Resources");

            using (var scene = new SortTestScene(count))
            using (var sorter = new GpuCountingSorter(shader, count))
            using (var commands = new CommandBuffer())
            {
                sorter.PrepareOnMainThread(scene.Input(), true);
                Assert.AreEqual(count, sorter.OrderedSplatCount);
                sorter.RecordCompute(commands);
                Graphics.ExecuteCommandBuffer(commands);

                var order = new List<uint>();
                yield return SortTestScene.ReadOrder(sorter.OrderTexture, count, order);
                scene.AssertBackToFront(order);
            }
        }
    }

    public sealed class CpuCountingSorterRuntimeTests
    {
        [UnityTest]
        public IEnumerator CpuOrderIsBackToFrontAfterCollecting([Values(1, 5000, 70000)] int count)
        {
            using (var scene = new SortTestScene(count))
            using (var sorter = new CpuCountingSorter(count))
            {
                sorter.PrepareOnMainThread(scene.Input(), true);
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
                sorter.PrepareOnMainThread(scene.Input(), true);
                yield return null;
                sorter.PrepareOnMainThread(scene.Input(), false); // collects the finished job, schedules nothing
                sorter.CompleteNow();
                Assert.AreEqual(3000, sorter.OrderedSplatCount);
                Assert.IsFalse(sorter.IsSorting);
            }
        }
    }
}
