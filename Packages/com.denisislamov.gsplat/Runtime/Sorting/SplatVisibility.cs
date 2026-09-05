using Unity.Burst;
using Unity.Mathematics;

namespace GSplat
{
    /// <summary>
    /// The per-splat visibility test the key pass runs (CPU and GPU: GSplatCore.hlsl has the twin). Splats that fail
    /// get no sort slot, so the draw only has instances that can produce pixels. Measured on a real capture viewed
    /// from inside: hundreds of thousands of sub-pixel quads cost 18 ms on an Apple GPU while contributing nothing
    /// visible, so this is the single most important culling step on tile-based GPUs.
    /// </summary>
    [BurstCompile]
    public static class SplatVisibility
    {
        /// <summary>How far outside the screen (in NDC units) a splat center may be and still draw; covers the largest reasonable radius.</summary>
        public const float NdcMargin = 0.3f;

        /// <summary>
        /// <paramref name="localToClip"/> = projection x view x localToWorld. <paramref name="focalPixelsY"/> = |P[1][1]| x height / 2.
        /// The radius is the splat's own (before the 0.3 px dilation): the largest half-axis projected at the splat's depth, a conservative upper bound.
        /// </summary>
        public static bool IsVisible(float3 positionLocal, float3 scale, in float4x4 localToClip, float focalPixelsY, float maxStdDev, float minPixelRadius)
        {
            float4 clip = math.mul(localToClip, new float4(positionLocal, 1f));
            // For a perspective camera w is the view depth; at or behind the camera plane nothing can be drawn.
            if (clip.w <= 1e-4f) return false;

            float radiusPixels = maxStdDev * math.cmax(scale) * focalPixelsY / clip.w;
            if (radiusPixels < minPixelRadius) return false;

            float2 ndc = clip.xy / clip.w;
            return math.all(math.abs(ndc) <= 1f + NdcMargin);
        }
    }
}
