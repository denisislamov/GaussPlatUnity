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
            using (SplatCloud cloud = TestCloudsRuntime.Random(count, 0, 3, 20f))
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

                    var centers = new NativeArray<float4>(data.ChunkCount, Allocator.Temp);
                    for (int chunkIndex = 0; chunkIndex < data.ChunkCount; chunkIndex++) centers[chunkIndex] = new float4(data.Chunks[chunkIndex].Center, 0f);
                    var centerBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, data.ChunkCount, 16);
                    centerBuffer.SetData(centers);
                    centers.Dispose();

                    var positions = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 16);
                    var scales = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 16);
                    var rotations = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 16);
                    var colors = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 16);

                    int kernel = shader.FindKernel("Unpack");
                    shader.SetTexture(kernel, "_Splats", gpu.SplatTexture);
                    shader.SetBuffer(kernel, "_ChunkCenters", centerBuffer);
                    shader.SetBuffer(kernel, "_Positions", positions);
                    shader.SetBuffer(kernel, "_Scales", scales);
                    shader.SetBuffer(kernel, "_Rotations", rotations);
                    shader.SetBuffer(kernel, "_Colors", colors);
                    shader.SetInt("_SplatCount", count);
                    shader.Dispatch(kernel, (count + 63) / 64, 1, 1);

                    AsyncGPUReadbackRequest positionRead = AsyncGPUReadback.Request(positions);
                    AsyncGPUReadbackRequest scaleRead = AsyncGPUReadback.Request(scales);
                    AsyncGPUReadbackRequest rotationRead = AsyncGPUReadback.Request(rotations);
                    AsyncGPUReadbackRequest colorRead = AsyncGPUReadback.Request(colors);
                    while (!positionRead.done || !scaleRead.done || !rotationRead.done || !colorRead.done) yield return null;
                    Assert.IsFalse(positionRead.hasError || scaleRead.hasError || rotationRead.hasError || colorRead.hasError, "readback failed");

                    NativeArray<float4> gpuPositions = positionRead.GetData<float4>();
                    NativeArray<float4> gpuScales = scaleRead.GetData<float4>();
                    NativeArray<float4> gpuRotations = rotationRead.GetData<float4>();
                    NativeArray<float4> gpuColors = colorRead.GetData<float4>();

                    for (int splatIndex = 0; splatIndex < count; splatIndex += 7)
                    {
                        PackedSplat.Unpack(data.Packed[splatIndex], out float3 relative, out float3 logScale, out float4 rotation, out float3 color, out float alpha);
                        float3 expectedPosition = relative + data.Chunks[splatIndex / SplatChunkInfo.Size].Center;
                        Assert.That(math.distance(expectedPosition, gpuPositions[splatIndex].xyz), Is.LessThan(1e-3f), "position " + splatIndex);
                        Assert.AreEqual(alpha, gpuPositions[splatIndex].w, 1e-5f, "alpha " + splatIndex);
                        Assert.That(math.distance(math.exp(logScale), gpuScales[splatIndex].xyz), Is.LessThan(1e-4f * math.cmax(math.exp(logScale)) + 1e-6f), "scale " + splatIndex);
                        Assert.That(math.distance(rotation, gpuRotations[splatIndex]), Is.LessThan(1e-4f), "rotation " + splatIndex);
                        Assert.That(math.distance(color, gpuColors[splatIndex].xyz), Is.LessThan(1e-5f), "color " + splatIndex);
                    }

                    centerBuffer.Dispose();
                    positions.Dispose();
                    scales.Dispose();
                    rotations.Dispose();
                    colors.Dispose();
                }
            }
        }
    }
}
