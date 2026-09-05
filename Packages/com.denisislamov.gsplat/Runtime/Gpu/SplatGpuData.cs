using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace GSplat
{
    /// <summary>
    /// GPU copies of a <see cref="GsplatData"/>: the packed splats in an RGBA8 texture (four 4-byte texels per splat,
    /// see <see cref="PackedSplat"/>) and the chunk table in a structured buffer. A texture rather than a StructuredBuffer
    /// because WebGL2 / GLES 3.0 vertex shaders cannot read buffers, and one data path keeps one shader for every platform.
    /// Upload is per chunk (<see cref="UploadNextChunk"/>) so a big scene can stream in over several frames.
    /// </summary>
    public sealed class SplatGpuData : IDisposable
    {
        /// <summary>4096 is the largest texture width every target guarantees (WebGL2, GLES 3.0).</summary>
        public const int TextureWidth = 4096;

        /// <summary>Rows one chunk occupies: 65536 splats x 4 texels / 4096 texels per row = 64.</summary>
        public const int RowsPerChunk = SplatChunkInfo.Size * PackedSplat.TexelsPerSplat / TextureWidth;

        public readonly int SplatCount;
        public readonly int ChunkCount;
        public readonly int ShDegree;
        public readonly bool Antialiased;
        public readonly Bounds LocalBounds;

        /// <summary>Four RGBA8 texels per splat: uint k of splat i is texel 4i + k in row-major order. Point filtered, linear (not sRGB).</summary>
        public Texture2D SplatTexture { get; private set; }

        /// <summary>One <see cref="SplatChunkInfo"/> per chunk.</summary>
        public GraphicsBuffer ChunkBuffer { get; private set; }

        /// <summary>Quantized SH bytes as a raw buffer, or null for degree 0. Consumed by the renderer in E3.</summary>
        public GraphicsBuffer ShBuffer { get; private set; }

        /// <summary>Chunks whose texels are already on the GPU: [0, UploadedChunkCount).</summary>
        public int UploadedChunkCount { get; private set; }

        public bool IsFullyUploaded => UploadedChunkCount == ChunkCount;

        /// <summary>Approximate GPU memory held by this object, for the memory budget (E6-T4).</summary>
        public long GpuMemoryBytes => (long)SplatTexture.width * SplatTexture.height * 4 + (long)ChunkBuffer.count * ChunkBuffer.stride + (ShBuffer != null ? (long)ShBuffer.count * ShBuffer.stride : 0);

        private readonly GsplatData source;
        private readonly bool canCopyRegions;
        private Texture2D stagingTexture;

        /// <summary>
        /// Creates the GPU resources but uploads nothing yet; call <see cref="UploadNextChunk"/> until
        /// <see cref="IsFullyUploaded"/> (or <see cref="UploadAll"/>). <paramref name="data"/> must stay alive
        /// and unmodified until the upload is complete; it is not disposed here.
        /// </summary>
        public SplatGpuData(GsplatData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            source = data;
            SplatCount = data.SplatCount;
            ChunkCount = data.ChunkCount;
            ShDegree = data.ShDegree;
            Antialiased = data.Antialiased;
            LocalBounds = new Bounds();
            LocalBounds.SetMinMax(data.BoundsMin, data.BoundsMax);

            int rows = math.max(1, ChunkCount * RowsPerChunk);
            SplatTexture = new Texture2D(TextureWidth, rows, GraphicsFormat.R8G8B8A8_UNorm, TextureCreationFlags.None)
            {
                name = "GSplat Splats",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0
            };

            canCopyRegions = (SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) != 0 && ChunkCount > 1;
            if (canCopyRegions)
            {
                // A Texture2D keeps a CPU mirror until Apply(makeNoLongerReadable: true). On the staging path the big
                // texture is only ever written by CopyTexture, so drop the mirror right away: it would double the memory.
                SplatTexture.Apply(false, true);
            }

            ChunkBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, math.max(1, ChunkCount), 48) { name = "GSplat Chunks" };
            if (ChunkCount > 0) ChunkBuffer.SetData(data.Chunks);

            if (data.Sh.Length > 0)
            {
                // Raw buffer of bytes packed 4 per uint; the shader unpacks with byte offsets. Padded to a whole
                // number of uints because a raw buffer cannot take a byte count that is not a multiple of 4.
                int uintCount = (data.Sh.Length + 3) / 4;
                ShBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, uintCount, 4) { name = "GSplat SH" };
                var padded = new NativeArray<byte>(uintCount * 4, Allocator.Temp, NativeArrayOptions.ClearMemory);
                NativeArray<byte>.Copy(data.Sh, padded, data.Sh.Length);
                ShBuffer.SetData(padded.Reinterpret<uint>(1));
                padded.Dispose();
            }
        }

        /// <summary>
        /// Uploads one chunk (64 texture rows, 1 MB). Uses a small staging texture and Graphics.CopyTexture so only
        /// that chunk crosses the bus; on GPUs without region copy support the whole texture is uploaded at once
        /// instead (fine on desktop, and those GPUs are not our mobile targets).
        /// </summary>
        public void UploadNextChunk()
        {
            if (IsFullyUploaded) return;

            int chunkIndex = UploadedChunkCount;
            int firstSplat = chunkIndex * SplatChunkInfo.Size;
            int splatCount = source.Chunks[chunkIndex].SplatCount;

            if (canCopyRegions)
            {
                if (stagingTexture == null)
                {
                    stagingTexture = new Texture2D(TextureWidth, RowsPerChunk, GraphicsFormat.R8G8B8A8_UNorm, TextureCreationFlags.None)
                    {
                        name = "GSplat Staging",
                        filterMode = FilterMode.Point,
                        hideFlags = HideFlags.HideAndDontSave
                    };
                }

                // One uint4 (16 bytes) of packed data is exactly four RGBA8 texels, so the texel array is the packed
                // array viewed as uint4s: no per-byte shuffling needed (little-endian: byte 0 lands in R).
                NativeArray<uint4> staging = stagingTexture.GetPixelData<uint4>(0);
                NativeArray<uint4>.Copy(source.Packed, firstSplat, staging, 0, splatCount);
                stagingTexture.Apply(false, false);

                int rowsUsed = (splatCount * PackedSplat.TexelsPerSplat + TextureWidth - 1) / TextureWidth;
                Graphics.CopyTexture(stagingTexture, 0, 0, 0, 0, TextureWidth, rowsUsed, SplatTexture, 0, 0, 0, chunkIndex * RowsPerChunk);
            }
            else
            {
                NativeArray<uint4> pixels = SplatTexture.GetPixelData<uint4>(0);
                NativeArray<uint4>.Copy(source.Packed, 0, pixels, 0, source.Packed.Length);
                SplatTexture.Apply(false, true);
                UploadedChunkCount = ChunkCount;
                return;
            }

            UploadedChunkCount++;
            if (IsFullyUploaded && stagingTexture != null)
            {
                UnityEngine.Object.Destroy(stagingTexture);
                stagingTexture = null;
            }
        }

        public void UploadAll()
        {
            while (!IsFullyUploaded) UploadNextChunk();
        }

        /// <summary>Texture coordinates of uint <paramref name="part"/> (0..3) of a splat.</summary>
        public static int2 TexelOf(int splatIndex, int part)
        {
            int texel = splatIndex * PackedSplat.TexelsPerSplat + part;
            return new int2(texel % TextureWidth, texel / TextureWidth);
        }

        public void Dispose()
        {
            if (SplatTexture != null) { UnityEngine.Object.Destroy(SplatTexture); SplatTexture = null; }
            if (stagingTexture != null) { UnityEngine.Object.Destroy(stagingTexture); stagingTexture = null; }
            ChunkBuffer?.Dispose();
            ChunkBuffer = null;
            ShBuffer?.Dispose();
            ShBuffer = null;
        }
    }
}
