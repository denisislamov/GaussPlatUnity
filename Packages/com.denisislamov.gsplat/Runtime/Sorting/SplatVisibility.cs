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
        /// <summary>
        /// The radius is the splat's own (before the 0.3 px dilation): the largest half-axis projected at the splat's depth, a conservative upper bound.
        /// A splat is off screen only when its whole quad is: phone captures have 10 m background splats whose center is
        /// far outside the view while they cover half the frame (culling those by center alone lost 3 dB against Spark).
        /// </summary>
        public static bool IsVisible(float3 positionLocal, float3 scale, in SplatCameraView view)
        {
            float4 clip = math.mul(view.LocalToClip, new float4(positionLocal, 1f));
            // For a perspective camera w is the view depth; at or behind the camera plane nothing can be drawn.
            if (clip.w <= 1e-4f) return false;

            float radiusPixels = view.MaxStdDev * math.cmax(scale) * view.FocalPixelsY / clip.w;
            if (radiusPixels < view.MinPixelRadius) return false;

            float2 ndc = clip.xy / clip.w;
            float2 marginNdc = radiusPixels * 2f / view.ScreenSize; // pixels to NDC: the screen is 2 NDC units wide
            return math.all(math.abs(ndc) <= 1f + marginNdc);
        }
    }
}
