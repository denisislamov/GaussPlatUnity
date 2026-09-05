using Unity.Burst;
using Unity.Mathematics;

namespace GSplat
{
    /// <summary>
    /// The 16-byte GPU layout of one splat, shared by the C# packer, the tests and the shaders (GSplatPacked.hlsl
    /// mirrors these functions; keep them in sync - the PlayMode test PackedSplatGpuTests checks it). On the GPU the
    /// four uints live in four consecutive RGBA8 texels (Unity cannot create integer-format Texture2Ds, and RGBA8 is
    /// the one format every target, WebGL2 included, samples exactly); the shader rebuilds each uint from 4 bytes. Positions are float16 relative to the center of the splat's chunk,
    /// which keeps the precision usable on scenes tens of meters across (see SplatSpatialSort for why chunks
    /// are spatially compact).
    ///
    /// uint0: pos.x f16 | pos.y f16 &lt;&lt; 16
    /// uint1: pos.z f16 | logScale.x u8 &lt;&lt; 16 | logScale.y u8 &lt;&lt; 24
    /// uint2: logScale.z u8 | rot.x u8 &lt;&lt; 8 | rot.y u8 &lt;&lt; 16 | rot.z u8 &lt;&lt; 24   (rotation "first three", w &gt;= 0)
    /// uint3: r u8 | g u8 &lt;&lt; 8 | b u8 &lt;&lt; 16 | alpha u8 &lt;&lt; 24                  (display color, not the raw SH0)
    ///
    /// Scale and rotation use the SPZ encodings so nothing is lost when the source is an SPZ file.
    /// TODO: the 8-bit rotation is coarser than SPZ v3's 10-bit smallest-three. Measure on a real scene whether
    /// thin splats show it; if so, an optional 4-byte rotation texture is the fix, not a wider main layout.
    /// </summary>
    [BurstCompile]
    public static class PackedSplat
    {
        public const int SizeInBytes = 16;

        /// <summary>RGBA8 texels per splat: one per uint of the layout.</summary>
        public const int TexelsPerSplat = 4;

        public static uint4 Pack(float3 positionRelativeToChunk, float3 logScale, float4 rotationXyzw, float3 displayColor, float alpha)
        {
            uint posX = math.f32tof16(positionRelativeToChunk.x);
            uint posY = math.f32tof16(positionRelativeToChunk.y);
            uint posZ = math.f32tof16(positionRelativeToChunk.z);

            uint scaleX = SpzQuantization.EncodeLogScale(logScale.x);
            uint scaleY = SpzQuantization.EncodeLogScale(logScale.y);
            uint scaleZ = SpzQuantization.EncodeLogScale(logScale.z);

            SpzQuantization.EncodeRotationFirstThree(rotationXyzw, out byte rotX, out byte rotY, out byte rotZ);

            uint r = EncodeUnorm8(displayColor.x);
            uint g = EncodeUnorm8(displayColor.y);
            uint b = EncodeUnorm8(displayColor.z);
            uint a = EncodeUnorm8(alpha);

            return new uint4(
                posX | (posY << 16),
                posZ | (scaleX << 16) | (scaleY << 24),
                scaleZ | ((uint)rotX << 8) | ((uint)rotY << 16) | ((uint)rotZ << 24),
                r | (g << 8) | (b << 16) | (a << 24));
        }

        public static void Unpack(uint4 packed, out float3 positionRelativeToChunk, out float3 logScale, out float4 rotationXyzw, out float3 displayColor, out float alpha)
        {
            positionRelativeToChunk = new float3(
                math.f16tof32(packed.x & 0xFFFF),
                math.f16tof32(packed.x >> 16),
                math.f16tof32(packed.y & 0xFFFF));

            logScale = new float3(
                SpzQuantization.DecodeLogScale((byte)((packed.y >> 16) & 0xFF)),
                SpzQuantization.DecodeLogScale((byte)((packed.y >> 24) & 0xFF)),
                SpzQuantization.DecodeLogScale((byte)(packed.z & 0xFF)));

            rotationXyzw = SpzQuantization.DecodeRotationFirstThree(
                (byte)((packed.z >> 8) & 0xFF),
                (byte)((packed.z >> 16) & 0xFF),
                (byte)((packed.z >> 24) & 0xFF));

            displayColor = new float3(
                (packed.w & 0xFF) / 255f,
                ((packed.w >> 8) & 0xFF) / 255f,
                ((packed.w >> 16) & 0xFF) / 255f);
            alpha = ((packed.w >> 24) & 0xFF) / 255f;
        }

        /// <summary>Raw SH0 coefficient to the color the shader displays: 0.5 + Sh0Scale * c, clamped to [0, 1].</summary>
        public static float3 DisplayColor(float3 sh0Coefficient)
        {
            // TODO: clamping throws away HDR values above 1 that some trainers produce; check on real scenes whether
            // highlights look dull compared with the reference viewer before adding a brightness scale.
            return math.saturate(0.5f + ShMath.Sh0Scale * sh0Coefficient);
        }

        private static uint EncodeUnorm8(float value)
        {
            return (uint)math.clamp((int)math.round(value * 255f), 0, 255);
        }
    }
}
