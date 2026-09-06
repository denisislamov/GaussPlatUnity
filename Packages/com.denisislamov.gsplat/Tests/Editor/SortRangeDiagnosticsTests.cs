using System.IO;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace GSplat.Tests
{
    /// <summary>Diagnostic on a real capture: how coarse is the 16-bit depth key at the comparison pose?</summary>
    public sealed class SortRangeDiagnosticsTests
    {
        [TestCase("hornedlizard", 0.05981445f, 1.148877f, -21.33558f)]
        [TestCase("racoonfamily", -11.69849f, 0.3f, -24f)]
        public void ReportKeyResolution(string sample, float cx, float cy, float cz)
        {
            string path = $"Assets/Samples/Niantic/{sample}.spz";
            if (!File.Exists(path)) Assert.Ignore("sample missing");
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<GaussianSplatAsset>(path);
            using (GsplatData data = asset.LoadData())
            {
                var camera = new float3(cx, cy, cz);
                var forward = new float3(0f, 0f, 1f);
                var visible = new NativeArray<int>(data.ChunkCount, Allocator.Temp);
                for (int c = 0; c < data.ChunkCount; c++) visible[c] = c;
                SplatSortKeys.DepthRange(data.Chunks, visible, camera, forward, false, out float paddedMin, out float paddedMax);

                float actualMin = float.MaxValue, actualMax = float.MinValue, largestScale = 0f;
                int inFront = 0;
                for (int i = 0; i < data.SplatCount; i++)
                {
                    PackedSplat.Unpack(data.Packed[i], out float3 normalized, out float3 logScale, out _, out _, out _);
                    float depth = SplatSortKeys.ViewDepth(data.Chunks[i / SplatChunkInfo.Size].PositionOf(normalized), camera, forward);
                    if (depth <= 0f) continue;
                    inFront++;
                    actualMin = math.min(actualMin, depth); actualMax = math.max(actualMax, depth);
                    largestScale = math.max(largestScale, math.cmax(math.exp(logScale)));
                }

                float chunkMaxExtent = 0f;
                for (int c = 0; c < data.ChunkCount; c++) chunkMaxExtent = math.max(chunkMaxExtent, math.cmax(data.Chunks[c].BoundsMax - data.Chunks[c].BoundsMin));

                Debug.Log($"GSplat sort range [{sample}]: padded {paddedMin:F1}..{paddedMax:F1} m (bucket {(paddedMax - paddedMin) / 65535f * 100f:F2} cm), actual in-front {actualMin:F2}..{actualMax:F1} m (bucket {(actualMax - actualMin) / 65535f * 100f:F2} cm), largest splat scale {largestScale:F1} m, largest padded chunk extent {chunkMaxExtent:F0} m, splats in front {inFront:N0}");
                visible.Dispose();
            }
        }
    }
}
