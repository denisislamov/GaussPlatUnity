using System.Collections;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace GSplat.Tests
{
    public sealed class GpuCountingSorterTests
    {
        public static ComputeShader LoadSortShader()
        {
            var shader = Resources.Load<ComputeShader>("GSplatCountingSort");
            if (shader == null) Assert.Ignore("GSplatCountingSort.compute is not in a Resources folder; the test cannot load it.");
            return shader;
        }

        [UnityTest]
        public IEnumerator GpuOrderMatchesTheCpuDefinition([Values(1, 5000, 300000)] int count)
        {
            if (!GpuCountingSorter.IsSupported) Assert.Ignore("No compute shaders on this device.");
            ComputeShader shader = LoadSortShader();

            var random = new Unity.Mathematics.Random(99);
            var positions = new NativeArray<float3>(count, Allocator.Persistent);
            var positions4 = new NativeArray<float4>(count, Allocator.Persistent);
            for (int splatIndex = 0; splatIndex < count; splatIndex++)
            {
                positions[splatIndex] = random.NextFloat3(-50f, 50f);
                positions4[splatIndex] = new float4(positions[splatIndex], 0f);
            }

            float3 cameraPosition = new float3(0f, 0f, -100f);
            float3 cameraForward = math.normalize(new float3(0.2f, -0.1f, 1f));
            float minDepth = float.MaxValue;
            float maxDepth = float.MinValue;
            for (int splatIndex = 0; splatIndex < count; splatIndex++)
            {
                float depth = SplatSortKeys.ViewDepth(positions[splatIndex], cameraPosition, cameraForward);
                minDepth = math.min(minDepth, depth);
                maxDepth = math.max(maxDepth, depth);
            }

            var positionBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 16);
            var orderBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 4);
            positionBuffer.SetData(positions4);

            var order = new NativeArray<uint>(count, Allocator.Persistent);
            using (var sorter = new GpuCountingSorter(shader))
            using (var commands = new CommandBuffer())
            {
                sorter.Record(commands, positionBuffer, count, orderBuffer, cameraPosition, cameraForward, minDepth, maxDepth);
                Graphics.ExecuteCommandBuffer(commands);
                AsyncGPUReadbackRequest readback = AsyncGPUReadback.Request(orderBuffer);
                while (!readback.done) yield return null;
                Assert.IsFalse(readback.hasError, "GPU readback failed");
                readback.GetData<uint>().CopyTo(order);
            }

            SortAssertions.AssertBackToFront(positions, order, cameraPosition, cameraForward);

            positionBuffer.Dispose();
            orderBuffer.Dispose();
            positions.Dispose();
            positions4.Dispose();
            order.Dispose();
        }
    }

    public static class SortAssertions
    {
        public static void AssertBackToFront(NativeArray<float3> positions, NativeArray<uint> order, float3 cameraPosition, float3 cameraForward)
        {
            int count = positions.Length;
            var seen = new bool[count];
            float minDepth = float.MaxValue;
            float maxDepth = float.MinValue;
            for (int splatIndex = 0; splatIndex < count; splatIndex++)
            {
                float depth = SplatSortKeys.ViewDepth(positions[splatIndex], cameraPosition, cameraForward);
                minDepth = math.min(minDepth, depth);
                maxDepth = math.max(maxDepth, depth);
            }

            float bucketSize = (maxDepth - minDepth) / SplatSortKeys.MaxKey;
            float previousDepth = float.MaxValue;
            for (int slot = 0; slot < count; slot++)
            {
                uint splatIndex = order[slot];
                Assert.That(splatIndex, Is.LessThan((uint)count), "index out of range at slot " + slot);
                Assert.IsFalse(seen[splatIndex], "index appears twice: " + splatIndex);
                seen[splatIndex] = true;

                float depth = SplatSortKeys.ViewDepth(positions[(int)splatIndex], cameraPosition, cameraForward);
                Assert.That(depth, Is.LessThanOrEqualTo(previousDepth + bucketSize * 1.01f + 1e-4f), "order breaks at slot " + slot);
                previousDepth = depth;
            }
        }
    }
}
