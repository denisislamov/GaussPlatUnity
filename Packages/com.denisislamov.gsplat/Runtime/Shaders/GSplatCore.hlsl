#ifndef GSPLAT_CORE_INCLUDED
#define GSPLAT_CORE_INCLUDED

// The splat rendering math, shared by the URP shader (and later by anything else that draws splats).
// Reference: Kerbl et al. 2023 "3D Gaussian Splatting", Zwicker et al. 2001 "EWA Splatting" for the projection,
// Yu et al. 2023 "Mip-Splatting" for the antialiasing term.

#include "GSplatPacked.hlsl"

#define GSPLAT_CHUNK_SIZE 65536u

// Rotation matrix of a unit quaternion (xyzw). Columns are the rotated basis vectors.
float3x3 GSplatQuaternionToMatrix(float4 q)
{
    float x = q.x, y = q.y, z = q.z, w = q.w;
    return float3x3(
        1.0 - 2.0 * (y * y + z * z), 2.0 * (x * y - w * z), 2.0 * (x * z + w * y),
        2.0 * (x * y + w * z), 1.0 - 2.0 * (x * x + z * z), 2.0 * (y * z - w * x),
        2.0 * (x * z - w * y), 2.0 * (y * z + w * x), 1.0 - 2.0 * (x * x + y * y));
}

// 3D covariance of the ellipsoid in world space: Sigma = M * M^T with M = ObjectToWorld * R * S.
// Building M first (instead of R S S^T R^T then transforming) handles object rotation and scale for free.
float3x3 GSplatWorldCovariance(float3x3 objectToWorld, float4 rotation, float3 scale)
{
    float3x3 rs = GSplatQuaternionToMatrix(rotation);
    rs[0] *= scale;  // scale the columns: rs = R * diag(scale) (rows of a float3x3 are indexed, so scale each row's components)
    rs[1] *= scale;
    rs[2] *= scale;
    float3x3 m = mul(objectToWorld, rs);
    return mul(m, transpose(m));
}

// Projects a world-space 3D covariance to a 2D screen-space covariance (in pixels^2) at view position t.
// J is the Jacobian of the perspective projection at t (EWA splatting, eq. 29). focal is P[0][0] * width / 2,
// P[1][1] * height / 2 with signs kept: a flipped render target flips fy, which mirrors the ellipse to match.
float3 GSplatProjectCovariance(float3x3 worldCovariance, float3x3 worldToView, float3 t, float2 focal)
{
    // Clamping x/z and y/z keeps the Jacobian sane for splats far outside the frustum (they are culled anyway
    // but their corners are still computed).
    float limitX = 1.3 * abs(focal.x) > 0.0 ? 1.3 : 1.3;
    float invZ = 1.0 / t.z;
    float tx = clamp(t.x * invZ, -limitX, limitX) * t.z;
    float ty = clamp(t.y * invZ, -limitX, limitX) * t.z;

    float3x3 j = float3x3(
        focal.x * invZ, 0.0, -focal.x * tx * invZ * invZ,
        0.0, focal.y * invZ, -focal.y * ty * invZ * invZ,
        0.0, 0.0, 0.0);
    float3x3 jw = mul(j, worldToView);
    float3x3 cov = mul(jw, mul(worldCovariance, transpose(jw)));
    return float3(cov[0][0], cov[0][1], cov[1][1]); // a, b, d of [[a b][b d]]
}

// Axes of the ellipse: eigenvectors/values of the symmetric 2x2 [[a b][b d]]. Written out with the degenerate
// cases (b ~ 0) handled explicitly, because normalize(0) is where most splat shaders get their sparkles from.
void GSplatEllipseAxes(float3 cov, out float2 majorAxis, out float lambdaMajor, out float lambdaMinor)
{
    float a = cov.x, b = cov.y, d = cov.z;
    float mid = 0.5 * (a + d);
    float det = a * d - b * b;
    float radius = sqrt(max(mid * mid - det, 1e-8));
    lambdaMajor = mid + radius;
    lambdaMinor = max(mid - radius, 1e-8);

    float2 v = float2(b, lambdaMajor - a);
    if (dot(v, v) < 1e-10) v = float2(lambdaMajor - d, b);
    if (dot(v, v) < 1e-10) v = a >= d ? float2(1.0, 0.0) : float2(0.0, 1.0);
    majorAxis = normalize(v);
}

// Reads SH coefficient c (0-based, above degree 0), channel ch (0..2) of a splat from the SH texture.
float GSplatShCoefficient(Texture2D<float4> shTexture, uint splatIndex, uint texelsPerSplat, uint coefficient, uint channel)
{
    uint byteIndex = coefficient * 3u + channel;
    uint texel = splatIndex * texelsPerSplat + byteIndex / 4u;
    float4 rgba = shTexture.Load(uint3(texel % GSPLAT_TEXTURE_WIDTH, texel / GSPLAT_TEXTURE_WIDTH, 0));
    float encoded = rgba[byteIndex % 4u] * 255.0;
    return (encoded - 128.0) / 128.0; // SpzQuantization.DecodeSh
}

float3 GSplatShCoefficient3(Texture2D<float4> shTexture, uint splatIndex, uint texelsPerSplat, uint coefficient)
{
    return float3(
        GSplatShCoefficient(shTexture, splatIndex, texelsPerSplat, coefficient, 0u),
        GSplatShCoefficient(shTexture, splatIndex, texelsPerSplat, coefficient, 1u),
        GSplatShCoefficient(shTexture, splatIndex, texelsPerSplat, coefficient, 2u));
}

// View-dependent color term from the real SH basis in the 3DGS order and constants; dir is the unit vector from
// the camera to the splat, in the same space as the coefficients (the object's local space).
float3 GSplatEvaluateSh(Texture2D<float4> shTexture, uint splatIndex, uint texelsPerSplat, uint degree, float3 dir)
{
    const float C1 = 0.4886025119029199;
    const float C2[5] = { 1.0925484305920792, -1.0925484305920792, 0.31539156525252005, -1.0925484305920792, 0.5462742152960396 };
    const float C3[7] = { -0.5900435899266435, 2.890611442640554, -0.4570457994644658, 0.3731763325901154, -0.4570457994644658, 1.445305721320277, -0.5900435899266435 };

    float x = dir.x, y = dir.y, z = dir.z;
    float3 result = 0;
    result += -C1 * y * GSplatShCoefficient3(shTexture, splatIndex, texelsPerSplat, 0u);
    result += C1 * z * GSplatShCoefficient3(shTexture, splatIndex, texelsPerSplat, 1u);
    result += -C1 * x * GSplatShCoefficient3(shTexture, splatIndex, texelsPerSplat, 2u);
    if (degree < 2u) return result;

    float xx = x * x, yy = y * y, zz = z * z, xy = x * y, yz = y * z, xz = x * z;
    result += C2[0] * xy * GSplatShCoefficient3(shTexture, splatIndex, texelsPerSplat, 3u);
    result += C2[1] * yz * GSplatShCoefficient3(shTexture, splatIndex, texelsPerSplat, 4u);
    result += C2[2] * (2.0 * zz - xx - yy) * GSplatShCoefficient3(shTexture, splatIndex, texelsPerSplat, 5u);
    result += C2[3] * xz * GSplatShCoefficient3(shTexture, splatIndex, texelsPerSplat, 6u);
    result += C2[4] * (xx - yy) * GSplatShCoefficient3(shTexture, splatIndex, texelsPerSplat, 7u);
    if (degree < 3u) return result;

    result += C3[0] * y * (3.0 * xx - yy) * GSplatShCoefficient3(shTexture, splatIndex, texelsPerSplat, 8u);
    result += C3[1] * xy * z * GSplatShCoefficient3(shTexture, splatIndex, texelsPerSplat, 9u);
    result += C3[2] * y * (4.0 * zz - xx - yy) * GSplatShCoefficient3(shTexture, splatIndex, texelsPerSplat, 10u);
    result += C3[3] * z * (2.0 * zz - 3.0 * xx - 3.0 * yy) * GSplatShCoefficient3(shTexture, splatIndex, texelsPerSplat, 11u);
    result += C3[4] * x * (4.0 * zz - xx - yy) * GSplatShCoefficient3(shTexture, splatIndex, texelsPerSplat, 12u);
    result += C3[5] * z * (xx - yy) * GSplatShCoefficient3(shTexture, splatIndex, texelsPerSplat, 13u);
    result += C3[6] * x * (xx - 3.0 * yy) * GSplatShCoefficient3(shTexture, splatIndex, texelsPerSplat, 14u);
    return result;
}

// Debug colour for chunk index visualisation: a cheap hash to a hue.
float3 GSplatChunkDebugColor(uint chunkIndex)
{
    float hue = frac(chunkIndex * 0.61803398875);
    float3 rgb = saturate(abs(frac(hue + float3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0) - 1.0);
    return rgb;
}

#endif
