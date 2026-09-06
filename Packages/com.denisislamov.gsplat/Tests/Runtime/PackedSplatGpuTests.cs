using System.Collections;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace GSplat.Tests
{
    /// <summary>Uploads a scene with SplatGpuData and unpacks it with the HLSL twin of PackedSplat; both sides must agree.</summary>
    public sealed class PackedSplatGpuTests
    {
        [UnityTest]
        public IEnumerator ShaderUnpackMatchesCSharpUnpack()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("No compute shaders on this device.");
            var shader = Resources.Load<ComputeShader>("GSplatUnpackTest");
            if (shader == null) Assert.Ignore("GSplatUnpackTest.compute is not in a Resources folder.");

            const int count = 70000; // two chunks, second one partial
            using (SplatCloud cloud = TestClouds.Random(count, 0, 3, 20f))
            {
                var options = new SplatImportOptions { SourceCoordinateSystem = SplatCoordinateSystem.Ruf, PruneAlphaBelow = 0f };
                using (GsplatData data = GsplatBuilder.Build(cloud, options))
                using (var gpu = new SplatGpuData(data))
                {
                    int uploads = 0;
                    while (!gpu.IsFullyUploaded)
                    {
                        gpu.UploadNextChunk();
                        uploads++;
                        yield return null;
                    }

                    Assert.That(uploads, Is.EqualTo(2).Or.EqualTo(1), "one upload per chunk, or one for everything on GPUs without region copy");


                    var positions = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 16);
                    var scales = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 16);
                    var rotations = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 16);
                    var colors = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 16);

                    int kernel = shader.FindKernel("Unpack");
                    shader.SetTexture(kernel, "_Splats", gpu.SplatTexture);
                    shader.SetTexture(kernel, "_ChunkRanges", gpu.ChunkRangeTexture);
                    shader.SetBuffer(kernel, "_Positions", positions);
                    shader.SetBuffer(kernel, "_Scales", scales);
                    shader.SetBuffer(kernel, "_Rotations", rotations);
                    shader.SetBuffer(kernel, "_Colors", colors);
                    shader.SetInt("_SplatCount", count);
                    shader.Dispatch(kernel, (count + 63) / 64, 1, 1);

                    // Synchronous readback: AsyncGPUReadback on these buffers reported hasError now and then in batch
                    // mode (no window, so frames are not flushed the usual way); GetData waits for the dispatch and
                    // always returns the data.
                    var gpuPositions = new float4[count];
                    var gpuScales = new float4[count];
                    var gpuRotations = new float4[count];
                    var gpuColors = new float4[count];
                    positions.GetData(gpuPositions);
                    scales.GetData(gpuScales);
                    rotations.GetData(gpuRotations);
                    colors.GetData(gpuColors);

                    for (int splatIndex = 0; splatIndex < count; splatIndex += 7)
                    {
                        PackedSplat.Unpack(data.Packed[splatIndex], out float3 normalized, out float3 logScale, out float4 rotation, out float3 color, out float alpha);
                        float3 expectedPosition = data.Chunks[splatIndex / SplatChunkInfo.Size].PositionOf(normalized);
                        Assert.That(math.distance(expectedPosition, gpuPositions[splatIndex].xyz), Is.LessThan(1e-3f), "position " + splatIndex);
                        Assert.AreEqual(alpha, gpuPositions[splatIndex].w, 1e-5f, "alpha " + splatIndex);
                        Assert.That(math.distance(math.exp(logScale), gpuScales[splatIndex].xyz), Is.LessThan(1e-4f * math.cmax(math.exp(logScale)) + 1e-6f), "scale " + splatIndex);
                        Assert.That(math.distance(rotation, gpuRotations[splatIndex]), Is.LessThan(1e-4f), "rotation " + splatIndex);
                        Assert.That(math.distance(color, gpuColors[splatIndex].xyz), Is.LessThan(1e-5f), "color " + splatIndex);
                    }

                    positions.Dispose();
                    scales.Dispose();
                    rotations.Dispose();
                    colors.Dispose();
                }
            }
        }
    }
}
