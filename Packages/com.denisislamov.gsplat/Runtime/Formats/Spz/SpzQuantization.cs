using Unity.Mathematics;

namespace GSplat
{
    /// <summary>
    /// The per-attribute encodings of the SPZ body, written out as small functions so the reader, the writer
    /// and the tests share one definition. Formulas follow the SPZ reference implementation exactly.
    /// </summary>
    public static class SpzQuantization
    {
        /// <summary>Scales are stored as round((log_scale + 10) * 16): log range [-10, 5.94] in 1/16 steps.</summary>
        public static byte EncodeLogScale(float logScale)
        {
            return (byte)math.clamp((int)math.round((logScale + 10f) * 16f), 0, 255);
        }

        public static float DecodeLogScale(byte encoded)
        {
            return encoded / 16f - 10f;
        }

        /// <summary>Alpha is stored as opacity (sigmoid already applied) in 8 bits.</summary>
        public static byte EncodeAlpha(float opacity)
        {
            return (byte)math.clamp((int)math.round(opacity * 255f), 0, 255);
        }

        public static float DecodeAlpha(byte encoded)
        {
            return encoded / 255f;
        }

        /// <summary>
        /// Color is the raw zeroth SH coefficient, stored as round(coefficient * Sh0Scale * 255 + 127.5).
        /// That is the displayable color (0.5 + Sh0Scale * c) in 8 bits, so values are clamped to [0, 1] here.
        /// </summary>
        public static byte EncodeColor(float sh0Coefficient)
        {
            return (byte)math.clamp((int)math.round(sh0Coefficient * ShMath.Sh0Scale * 255f + 127.5f), 0, 255);
        }

        public static float DecodeColor(byte encoded)
        {
            return (encoded / 255f - 0.5f) / ShMath.Sh0Scale;
        }

        /// <summary>Higher SH coefficients: round(x * 128 + 128), i.e. [-1, 1) in 1/128 steps.</summary>
        public static byte EncodeSh(float coefficient)
        {
            return (byte)math.clamp((int)math.round(coefficient * 128f + 128f), 0, 255);
        }

        public static float DecodeSh(byte encoded)
        {
            return (encoded - 128) / 128f;
        }

        /// <summary>Positions are signed 24-bit fixed point: value * 2^fractionalBits, rounded, clamped to the int24 range.</summary>
        public static int EncodePosition(float value, int fractionalBits)
        {
            return math.clamp((int)math.round(value * (1 << fractionalBits)), -(1 << 23), (1 << 23) - 1);
        }

        /// <summary>Reads 3 little-endian bytes as a sign-extended 24-bit integer and scales it back.</summary>
        public static float DecodePosition(byte b0, byte b1, byte b2, int fractionalBits)
        {
            int fixedPoint = b0 | (b1 << 8) | (b2 << 16);
            if ((fixedPoint & 0x800000) != 0)
            {
                fixedPoint |= unchecked((int)0xFF000000); // sign-extend bit 23 into the upper byte
            }

            return fixedPoint / (float)(1 << fractionalBits);
        }

        /// <summary>
        /// Rotation before SPZ version 3: xyz as round(q * 127.5 + 127.5), w dropped and recovered as
        /// +sqrt(1 - |xyz|^2). The quaternion is flipped to w >= 0 first so the sign of w is never needed.
        /// </summary>
        public static void EncodeRotationFirstThree(float4 xyzw, out byte x, out byte y, out byte z)
        {
            float4 q = math.normalize(xyzw);
            if (q.w < 0f) q = -q;
            x = (byte)math.clamp((int)math.round(q.x * 127.5f + 127.5f), 0, 255);
            y = (byte)math.clamp((int)math.round(q.y * 127.5f + 127.5f), 0, 255);
            z = (byte)math.clamp((int)math.round(q.z * 127.5f + 127.5f), 0, 255);
        }

        public static float4 DecodeRotationFirstThree(byte x, byte y, byte z)
        {
            float3 xyz = new float3(x, y, z) / 127.5f - 1f;
            float w = math.sqrt(math.max(0f, 1f - math.lengthsq(xyz)));
            return new float4(xyz, w);
        }

        /// <summary>
        /// Rotation from SPZ version 3 on, "smallest three": the largest component is dropped (its index goes in
        /// the top 2 bits, its sign is folded into the others so it is always positive); the other three are
        /// stored in increasing index order as 1 sign bit + 9-bit magnitude scaled by sqrt(2) (they cannot
        /// exceed 1/sqrt(2) when they are not the largest). Layout: [largest:2][c0:10][c1:10][c2:10], c0 highest.
        /// </summary>
        public static uint EncodeRotationSmallestThree(float4 xyzw)
        {
            float4 q = math.normalize(xyzw);
            int largest = 0;
            for (int component = 1; component < 4; component++)
            {
                if (math.abs(q[component]) > math.abs(q[largest])) largest = component;
            }

            bool negate = q[largest] < 0f;
            uint packed = (uint)largest;
            for (int component = 0; component < 4; component++)
            {
                if (component == largest) continue;

                uint signBit = ((q[component] < 0f) ^ negate) ? 1u : 0u;
                // |q| <= 1/sqrt(2) for a non-largest component, so |q| * sqrt(2) fills the [0, 1] range of the 9 bits.
                uint magnitude = math.min((uint)(511f * math.abs(q[component]) * math.SQRT2 + 0.5f), 511u);
                packed = (packed << 10) | (signBit << 9) | magnitude;
            }

            return packed;
        }

        public static float4 DecodeRotationSmallestThree(uint packed)
        {
            int largest = (int)(packed >> 30);
            float4 q = float4.zero;
            float sumOfSquares = 0f;
            int shift = 20; // the first stored component (lowest index) sits in the highest 10-bit slot
            for (int component = 0; component < 4; component++)
            {
                if (component == largest) continue;

                uint field = (packed >> shift) & 0x3FF;
                float magnitude = (field & 0x1FF) / 511f / math.SQRT2; // back to [0, 1/sqrt(2)]
                float value = (field & 0x200) != 0 ? -magnitude : magnitude;
                q[component] = value;
                sumOfSquares += value * value;
                shift -= 10;
            }

            q[largest] = math.sqrt(math.max(0f, 1f - sumOfSquares));
            return q;
        }
    }
}
