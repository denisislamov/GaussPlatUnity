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

        /// <summary>
        /// One <see cref="SplatChunkInfo"/> per chunk, for the compute sorter. Null on devices without compute shaders
        /// (WebGL2, GLES 3.0): structured buffers do not exist there, and nothing else reads this one.
        /// </summary>
        public GraphicsBuffer ChunkBuffer { get; private set; }

        /// <summary>
        /// Quantized SH bytes as an RGBA8 texture (same trick as the splats: a byte per channel), or null for degree 0.
        /// Splat i uses texels [i * ShTexelsPerSplat, +ShTexelsPerSplat); byte j of its SH is texel j / 4, channel j % 4.
        /// </summary>
        public Texture2D ShTexture { get; private set; }

        /// <summary>Texels per splat in <see cref="ShTexture"/>: 3, 6 or 12 for degrees 1, 2, 3 (9, 24, 45 bytes rounded up to 4).</summary>
        public int ShTexelsPerSplat { get; private set; }

        /// <summary>
        /// Chunk position ranges as an RGBAFloat texture, two texels per chunk: texel 2c = PositionMin, texel 2c+1 = PositionExtent.
        /// A texture rather than a uniform array because GLES 3.0 guarantees very few uniform vectors.
        /// </summary>
        public Texture2D ChunkRangeTexture { get; private set; }

        /// <summary>Chunks whose texels are already on the GPU: [0, UploadedChunkCount).</summary>
        public int UploadedChunkCount { get; private set; }

        public bool IsFullyUploaded => UploadedChunkCount == ChunkCount;

        /// <summary>Approximate GPU memory held by this object, for the memory budget (E6-T4).</summary>
        public long GpuMemoryBytes => (long)SplatTexture.width * SplatTexture.height * 4 + (ChunkBuffer != null ? (long)ChunkBuffer.count * ChunkBuffer.stride : 0)
            + (ShTexture != null ? (long)ShTexture.width * ShTexture.height * 4 : 0) + (long)ChunkRangeTexture.width * 16;

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

            // Region copies let a chunk be uploaded on its own; without them (or with a single chunk) the whole texture
            // goes up in one Apply, see UploadNextChunk.
            canCopyRegions = (SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) != 0 && ChunkCount > 1;

            CreateSplatTexture();
            CreateChunkResources(data);
            if (data.Sh.Length > 0) CreateShTexture(data);
        }

        private void CreateSplatTexture()
        {
            int rows = math.max(1, ChunkCount * RowsPerChunk);
            SplatTexture = new Texture2D(TextureWidth, rows, GraphicsFormat.R8G8B8A8_UNorm, TextureCreationFlags.None)
            {
                name = "GSplat Splats",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0
            };

            if (canCopyRegions)
            {
                // A Texture2D keeps a CPU mirror until Apply(makeNoLongerReadable: true). On the staging path the big
                // texture is only ever written by CopyTexture, so drop the mirror right away: it would double the memory.
                SplatTexture.Apply(false, true);
            }
        }

        /// <summary>The chunk table for compute (structured buffer) and for the vertex shader (range texture; GLES 3.0 has too few uniforms for an array).</summary>
        private void CreateChunkResources(GsplatData data)
        {
            if (SystemInfo.supportsComputeShaders)
            {
                ChunkBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, math.max(1, ChunkCount), 48) { name = "GSplat Chunks" };
                if (ChunkCount > 0) ChunkBuffer.SetData(data.Chunks);
            }

            ChunkRangeTexture = new Texture2D(math.max(2, ChunkCount * 2), 1, GraphicsFormat.R32G32B32A32_SFloat, TextureCreationFlags.None)
            {
                name = "GSplat Chunk Ranges",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            NativeArray<float4> ranges = ChunkRangeTexture.GetPixelData<float4>(0);
            for (int chunkIndex = 0; chunkIndex < ChunkCount; chunkIndex++)
            {
                ranges[chunkIndex * 2] = new float4(data.Chunks[chunkIndex].PositionMin, 0f);
                ranges[chunkIndex * 2 + 1] = new float4(data.Chunks[chunkIndex].PositionExtent, 0f);
            }

            ChunkRangeTexture.Apply(false, true);
        }

        private void CreateShTexture(GsplatData data)
        {
            ShTexelsPerSplat = (data.ShBytesPerSplat + 3) / 4;
            long texelCount = (long)SplatCount * ShTexelsPerSplat;
            int rows = (int)((texelCount + TextureWidth - 1) / TextureWidth);
            if (rows > 4096)
            {
                // 4096 x 4096 texels = 1.39M splats at degree 3, 2.8M at degree 2. Above that the SH is simply not loaded.
                // TODO: a second SH texture would lift the limit; decide once a real scene needs it.
                Debug.LogWarning($"GSplat: {SplatCount:N0} splats at SH degree {ShDegree} do not fit one SH texture; rendering without view-dependent color.");
                ShTexelsPerSplat = 0;
                return;
            }

            ShTexture = new Texture2D(TextureWidth, rows, GraphicsFormat.R8G8B8A8_UNorm, TextureCreationFlags.None)
            {
                name = "GSplat SH",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            // Per splat: ShBytesPerSplat bytes, then padding up to a whole texel. A straight copy works when the byte
            // count is already a multiple of 4 (degree 2: 24 bytes); degrees 1 and 3 (9 and 45) need the per-splat gap.
            NativeArray<byte> texels = ShTexture.GetPixelData<byte>(0);
            int strideBytes = ShTexelsPerSplat * 4;
            for (int splatIndex = 0; splatIndex < SplatCount; splatIndex++)
            {
                NativeArray<byte>.Copy(data.Sh, splatIndex * data.ShBytesPerSplat, texels, splatIndex * strideBytes, data.ShBytesPerSplat);
            }

            ShTexture.Apply(false, true);
        }

        /// <summary>
        /// Uploads one chunk (64 texture rows, 1 MB). Uses a small staging texture and Graphics.CopyTexture so only
        /// that chunk crosses the bus; on GPUs without region copy support the whole texture is uploaded at once
        /// instead (fine on desktop, and those GPUs are not our mobile targets).
        /// </summary>
        public void UploadNextChunk()
        {
            if (IsFullyUploaded) return;

            if (canCopyRegions) UploadChunkThroughStaging(UploadedChunkCount);
            else UploadEverythingAtOnce();
        }

        private void UploadChunkThroughStaging(int chunkIndex)
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

            int firstSplat = chunkIndex * SplatChunkInfo.Size;
            int splatCount = source.Chunks[chunkIndex].SplatCount;

            // One uint4 (16 bytes) of packed data is exactly four RGBA8 texels, so the texel array is the packed
            // array viewed as uint4s: no per-byte shuffling needed (little-endian: byte 0 lands in R).
            NativeArray<uint4> staging = stagingTexture.GetPixelData<uint4>(0);
            NativeArray<uint4>.Copy(source.Packed, firstSplat, staging, 0, splatCount);
            stagingTexture.Apply(false, false);

            int rowsUsed = (splatCount * PackedSplat.TexelsPerSplat + TextureWidth - 1) / TextureWidth;
            Graphics.CopyTexture(stagingTexture, 0, 0, 0, 0, TextureWidth, rowsUsed, SplatTexture, 0, 0, 0, chunkIndex * RowsPerChunk);

            UploadedChunkCount++;
            if (IsFullyUploaded)
            {
                SplatObjectUtility.Destroy(stagingTexture);
                stagingTexture = null;
            }
        }

        private void UploadEverythingAtOnce()
        {
            NativeArray<uint4> pixels = SplatTexture.GetPixelData<uint4>(0);
            NativeArray<uint4>.Copy(source.Packed, 0, pixels, 0, source.Packed.Length);
            SplatTexture.Apply(false, true);
            UploadedChunkCount = ChunkCount;
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
            // Textures first, the chunk buffer last. TODO: with the buffer disposed before the textures, a PlayMode test
            // that reads back compute results right after another SplatGpuData was disposed failed intermittently on
            // Metal (AsyncGPUReadback hasError). Texture destruction is deferred while GraphicsBuffer.Dispose is
            // immediate; why the order matters is not understood, so it is pinned here and not to be shuffled.
            if (SplatTexture != null) { SplatObjectUtility.Destroy(SplatTexture); SplatTexture = null; }
            if (stagingTexture != null) { SplatObjectUtility.Destroy(stagingTexture); stagingTexture = null; }
            if (ShTexture != null) { SplatObjectUtility.Destroy(ShTexture); ShTexture = null; }
            if (ChunkRangeTexture != null) { SplatObjectUtility.Destroy(ChunkRangeTexture); ChunkRangeTexture = null; }
            ChunkBuffer?.Dispose();
            ChunkBuffer = null;
        }
    }
}
