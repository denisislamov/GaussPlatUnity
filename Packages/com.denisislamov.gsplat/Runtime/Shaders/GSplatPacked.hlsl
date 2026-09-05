#ifndef GSPLAT_PACKED_INCLUDED
#define GSPLAT_PACKED_INCLUDED

// Shader-side twin of PackedSplat.cs. Keep the two in sync; PackedSplatGpuTests compares them.
//   uint0: pos.x f16 | pos.y f16 << 16
//   uint1: pos.z f16 | logScale.x u8 << 16 | logScale.y u8 << 24
//   uint2: logScale.z u8 | rot.x u8 << 8 | rot.y u8 << 16 | rot.z u8 << 24
//   uint3: r u8 | g u8 << 8 | b u8 << 16 | alpha u8 << 24

#define GSPLAT_TEXTURE_WIDTH 4096

struct GSplatUnpacked
{
    float3 position;   // relative to the chunk center
    float3 scale;      // ellipsoid half-axes (exp of the log scale)
    float4 rotation;   // xyzw, w >= 0
    float3 color;      // display color, 0..1
    float alpha;       // 0..1
};

// Four RGBA8 texels per splat, one per uint of the layout: uint k of splat i is texel 4i + k, row-major, width
// 4096. RGBA8 because it is the one format every target samples exactly; Unity cannot make integer Texture2Ds.
// The z of the result is the mip level for Texture2D.Load.
uint3 GSplatTexelOf(uint splatIndex, uint part)
{
    uint texel = splatIndex * 4u + part;
    return uint3(texel % GSPLAT_TEXTURE_WIDTH, texel / GSPLAT_TEXTURE_WIDTH, 0);
}

// RGBA8 UNorm texel back to the uint it stores: R is byte 0 (little-endian). x * 255 is exact for 8-bit values up
// to rounding noise, hence the + 0.5 before truncation.
uint GSplatTexelToUint(float4 texel)
{
    uint4 bytes = (uint4)(texel * 255.0 + 0.5);
    return bytes.x | (bytes.y << 8) | (bytes.z << 16) | (bytes.w << 24);
}

// The inverse of GSplatTexelToUint: a uint as an RGBA8 texel (for compute shaders writing the order texture).
float4 GSplatUintToTexel(uint value)
{
    return float4(value & 0xFFu, (value >> 8) & 0xFFu, (value >> 16) & 0xFFu, (value >> 24) & 0xFFu) / 255.0;
}

// The 16 bytes of splat i as the uint4 PackedSplat.cs describes.
uint4 GSplatLoadPacked(Texture2D<float4> splats, uint splatIndex)
{
    return uint4(
        GSplatTexelToUint(splats.Load(GSplatTexelOf(splatIndex, 0))),
        GSplatTexelToUint(splats.Load(GSplatTexelOf(splatIndex, 1))),
        GSplatTexelToUint(splats.Load(GSplatTexelOf(splatIndex, 2))),
        GSplatTexelToUint(splats.Load(GSplatTexelOf(splatIndex, 3))));
}

float GSplatDecodeLogScale(uint encoded)
{
    return encoded / 16.0 - 10.0;
}

// "First three" rotation: xyz in [-1, 1] from 8 bits, w recovered as +sqrt(1 - |xyz|^2).
float4 GSplatDecodeRotation(uint x, uint y, uint z)
{
    float3 xyz = float3(x, y, z) / 127.5 - 1.0;
    float w = sqrt(max(0.0, 1.0 - dot(xyz, xyz)));
    return float4(xyz, w);
}

GSplatUnpacked GSplatUnpack(uint4 lo)
{
    GSplatUnpacked s;
    s.position = float3(f16tof32(lo.x & 0xFFFF), f16tof32(lo.x >> 16), f16tof32(lo.y & 0xFFFF));
    s.scale = exp(float3(
        GSplatDecodeLogScale((lo.y >> 16) & 0xFF),
        GSplatDecodeLogScale((lo.y >> 24) & 0xFF),
        GSplatDecodeLogScale(lo.z & 0xFF)));
    s.rotation = GSplatDecodeRotation((lo.z >> 8) & 0xFF, (lo.z >> 16) & 0xFF, (lo.z >> 24) & 0xFF);
    s.color = float3(lo.w & 0xFF, (lo.w >> 8) & 0xFF, (lo.w >> 16) & 0xFF) / 255.0;
    s.alpha = ((lo.w >> 24) & 0xFF) / 255.0;
    return s;
}

#endif
