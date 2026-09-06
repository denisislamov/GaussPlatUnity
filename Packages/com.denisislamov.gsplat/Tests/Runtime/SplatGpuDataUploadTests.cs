using System.Collections;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace GSplat.Tests
{
    /// <summary>
    /// The chunk-by-chunk upload must land every splat's four texels where the shader expects them. Reads the splat
    /// texture back after each chunk and compares it with the packed source; covers the staging + CopyTexture path
    /// on this machine and the whole-texture fallback on GPUs without region copies (the assertions are the same).
    /// </summary>
    public sealed class SplatGpuDataUploadTests
    {
        [UnityTest]
        public IEnumerator EveryUploadedChunkMatchesThePackedSource()
        {
            const int count = 2 * SplatChunkInfo.Size + 1234; // three chunks, the last one partial
            using (SplatCloud cloud = TestClouds.Random(count, 0, 7, 30f))
            {
                var options = new SplatImportOptions { SourceCoordinateSystem = SplatCoordinateSystem.Ruf, PruneAlphaBelow = 0f };
                using (GsplatData data = GsplatBuilder.Build(cloud, options))
                using (var gpu = new SplatGpuData(data))
                {
                    Assert.AreEqual(3, gpu.ChunkCount);
                    Assert.AreEqual(0, gpu.UploadedChunkCount);

                    while (!gpu.IsFullyUploaded)
                    {
                        int before = gpu.UploadedChunkCount;
                        gpu.UploadNextChunk();
                        yield return null;
                        Assert.Greater(gpu.UploadedChunkCount, before, "every call uploads at least one chunk");

                        AsyncGPUReadbackRequest readback = AsyncGPUReadback.Request(gpu.SplatTexture, 0, TextureFormat.RGBA32);
                        while (!readback.done) yield return null;
                        Assert.IsFalse(readback.hasError, "readback failed");

                        NativeArray<uint4> texels = readback.GetData<uint4>();
                        int uploadedSplats = math.min(count, gpu.UploadedChunkCount * SplatChunkInfo.Size);
                        for (int splatIndex = 0; splatIndex < uploadedSplats; splatIndex++)
                        {
                            if (!texels[splatIndex].Equals(data.Packed[splatIndex]))
                            {
                                Assert.Fail($"splat {splatIndex} differs after {gpu.UploadedChunkCount} uploaded chunk(s)");
                            }
                        }
                    }

                    // Calling again once everything is up must be a no-op.
                    gpu.UploadNextChunk();
                    Assert.AreEqual(3, gpu.UploadedChunkCount);
                }
            }
        }
    }
}
