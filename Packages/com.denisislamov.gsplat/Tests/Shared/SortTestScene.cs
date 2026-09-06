using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace GSplat.Tests
{
    /// <summary>A random multi-chunk scene on the GPU plus everything a sorter needs, shared by the sort tests.</summary>
    public sealed class SortTestScene : System.IDisposable
    {
        public readonly GsplatData Data;
        public readonly SplatGpuData Gpu;
        public readonly NativeArray<int> VisibleChunks;
        public readonly GraphicsBuffer VisibleChunkBuffer;
        public readonly float3 CameraPosition = new float3(0f, 0f, -100f);
        public readonly float3 CameraForward;
        public readonly float3[] Positions;

        public SortTestScene(int splatCount, uint seed = 99)
        {
            CameraForward = math.normalize(new float3(0.2f, -0.1f, 1f));
            using (SplatCloud cloud = TestClouds.Random(splatCount, 0, seed, 50f))
            {
                var options = new SplatImportOptions { SourceCoordinateSystem = SplatCoordinateSystem.Ruf, PruneAlphaBelow = 0f };
                Data = GsplatBuilder.Build(cloud, options);
            }

            Gpu = new SplatGpuData(Data);
            Gpu.UploadAll();

            VisibleChunks = new NativeArray<int>(Data.ChunkCount, Allocator.Persistent);
            for (int chunkIndex = 0; chunkIndex < Data.ChunkCount; chunkIndex++) VisibleChunks[chunkIndex] = chunkIndex;
            VisibleChunkBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Data.ChunkCount, sizeof(int));
            VisibleChunkBuffer.SetData(VisibleChunks);

            // Reference positions as the packer stored them (quantized), so the assertion checks the sort, not the packing.
            Positions = new float3[Data.SplatCount];
            for (int splatIndex = 0; splatIndex < Data.SplatCount; splatIndex++)
            {
                PackedSplat.Unpack(Data.Packed[splatIndex], out float3 normalized, out _, out _, out _, out _);
                Positions[splatIndex] = Data.Chunks[splatIndex / SplatChunkInfo.Size].PositionOf(normalized);
            }
        }

        public bool Radial;

        public SplatSortInput Input()
        {
            SplatSortKeys.DepthRange(Data.Chunks, VisibleChunks, CameraPosition, CameraForward, Radial, out float minDepth, out float maxDepth);
            return new SplatSortInput
            {
                Radial = Radial,
                Data = Data,
                Gpu = Gpu,
                VisibleChunks = VisibleChunks,
                VisibleChunkBuffer = VisibleChunkBuffer,
                VisibleSplatCount = Data.SplatCount,
                CameraPositionLocal = CameraPosition,
                CameraForwardLocal = CameraForward,
                MinDepth = minDepth,
                MaxDepth = maxDepth
            };
        }

        /// <summary>Reads the first <paramref name="count"/> slots of an order texture (Texture2D or RenderTexture) back as splat indices.</summary>
        public static IEnumerator ReadOrder(Texture orderTexture, int count, List<uint> result)
        {
            NativeArray<byte> bytes;
            if (orderTexture is Texture2D readable)
            {
                bytes = readable.GetPixelData<byte>(0);
                Decode(bytes, count, result);
                yield break;
            }

            AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(orderTexture, 0, TextureFormat.RGBA32);
            while (!request.done) yield return null;
            Assert.IsFalse(request.hasError, "order texture readback failed");
            bytes = request.GetData<byte>();
            Decode(bytes, count, result);
        }

        private static void Decode(NativeArray<byte> bytes, int count, List<uint> result)
        {
            result.Clear();
            for (int slot = 0; slot < count; slot++)
            {
                int byteIndex = slot * 4;
                result.Add((uint)(bytes[byteIndex] | (bytes[byteIndex + 1] << 8) | (bytes[byteIndex + 2] << 16) | (bytes[byteIndex + 3] << 24)));
            }
        }

        /// <summary>Depth must not increase along the order (farthest first) and every splat must appear exactly once.</summary>
        public void AssertBackToFront(List<uint> order)
        {
            Assert.AreEqual(Data.SplatCount, order.Count, "slot count");
            var seen = new bool[Data.SplatCount];
            SplatSortKeys.DepthRange(Data.Chunks, VisibleChunks, CameraPosition, CameraForward, Radial, out float minDepth, out float maxDepth);
            SplatSortKeys.LogRange(minDepth, maxDepth, out _, out float inverseLogRange);
            // Keys are linear in log(depth): one bucket spans depth * (ratio - 1).
            float ratio = math.exp(1f / (inverseLogRange * SplatSortKeys.MaxKey));
            float previousDepth = float.MaxValue;
            for (int slot = 0; slot < order.Count; slot++)
            {
                uint splatIndex = order[slot];
                Assert.That(splatIndex, Is.LessThan((uint)Data.SplatCount), "index out of range at slot " + slot);
                Assert.IsFalse(seen[splatIndex], "index appears twice: " + splatIndex);
                seen[splatIndex] = true;

                float depth = SplatSortKeys.SortMetric(Positions[splatIndex], CameraPosition, CameraForward, Radial);
                float bucketSize = previousDepth == float.MaxValue ? 0f : math.max(previousDepth, SplatSortKeys.MinKeyDepth) * (ratio - 1f);
                Assert.That(depth, Is.LessThanOrEqualTo(previousDepth + bucketSize * 1.01f + 1e-3f), "order breaks at slot " + slot);
                previousDepth = depth;
            }
        }

        public void Dispose()
        {
            VisibleChunkBuffer.Dispose();
            VisibleChunks.Dispose();
            Gpu.Dispose();
            Data.Dispose();
        }
    }
}
