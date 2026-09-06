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
    // but their corners are still computed). 1.3 = the 3DGS rasterizer's limit (a bit outside a 90 degree view).
    const float limit = 1.3;
    float invZ = 1.0 / t.z;
    float tx = clamp(t.x * invZ, -limit, limit) * t.z;
    float ty = clamp(t.y * invZ, -limit, limit) * t.z;

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

// The quad a splat gets on screen: ellipse axes in pixels after the low-pass dilation and the size clamp, plus the
// opacity factor that goes with the dilation. visible is false when the splat's own radius is under the threshold.
struct GSplatFootprint
{
    float2 majorAxis;    // unit vector in pixels
    float radiusMajor;   // half-extent along majorAxis, pixels
    float radiusMinor;   // half-extent across it, pixels
    float compensation;  // mip-splatting opacity scale (1 when off)
    bool visible;
};

// From the projected 2D covariance (a, b, d in pixels^2) to the quad. Steps, in order:
// 1. Own radius (largest eigenvalue before dilation): below minPixelRadius the splat is skipped. The key pass
//    already dropped most of these with a looser bound; this is the exact check.
// 2. Low-pass filter: every splat covers at least ~one pixel so thin ones do not flicker. With mip-splatting data
//    the opacity is scaled down to keep the total energy (Yu et al. 2023, eq. 7); classic 3DGS data was trained
//    with the dilation and no compensation, so we reproduce that.
// 3. Ellipse axes, then the size clamp: same shape, smaller footprint, the falloff is compressed into the clamped
//    quad and the opacity is unchanged.
GSplatFootprint GSplatScreenFootprint(float3 cov, float maxStdDev, float minPixelRadius, float dilation, float maxPixelRadius, bool antialiased)
{
    GSplatFootprint fp;
    fp.majorAxis = float2(1.0, 0.0);
    fp.radiusMajor = 0.0;
    fp.radiusMinor = 0.0;
    fp.compensation = 1.0;
    fp.visible = false;

    float detBefore = cov.x * cov.z - cov.y * cov.y;
    float midBefore = 0.5 * (cov.x + cov.z);
    float lambdaBefore = midBefore + sqrt(max(midBefore * midBefore - detBefore, 0.0));
    if (maxStdDev * sqrt(lambdaBefore) < minPixelRadius) return fp;

    cov.x += dilation;
    cov.z += dilation;
    float detAfter = cov.x * cov.z - cov.y * cov.y;
    fp.compensation = antialiased && dilation > 0.0 ? sqrt(max(detBefore / detAfter, 0.0)) : 1.0;

    float lambdaMajor, lambdaMinor;
    GSplatEllipseAxes(cov, fp.majorAxis, lambdaMajor, lambdaMinor);
    fp.radiusMajor = maxStdDev * sqrt(lambdaMajor);
    fp.radiusMinor = maxStdDev * sqrt(lambdaMinor);
    if (maxPixelRadius > 0.0 && fp.radiusMajor > maxPixelRadius)
    {
        float shrink = maxPixelRadius / fp.radiusMajor;
        fp.radiusMajor *= shrink;
        fp.radiusMinor *= shrink;
    }

    fp.visible = true;
    return fp;
}

// Pixel offset of a quad corner (each component -1 or +1) from the splat center.
float2 GSplatCornerOffsetPixels(float2 corner, GSplatFootprint fp)
{
    float2 minorAxis = float2(-fp.majorAxis.y, fp.majorAxis.x);
    return corner.x * fp.majorAxis * fp.radiusMajor + corner.y * minorAxis * fp.radiusMinor;
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

// Twin of SplatVisibility.IsVisible (C#): can this splat produce any pixel? Behind the camera, entirely off screen
// (center further out than its own projected radius) or with an own radius below the threshold -> no.
bool GSplatKeyPassVisible(float3 positionLocal, float3 scale, float4x4 localToClip, float focalPixelsY, float2 screenSize, float maxStdDev, float minPixelRadius)
{
    float4 clip = mul(localToClip, float4(positionLocal, 1.0));
    if (clip.w <= 1e-4) return false;

    float radiusPixels = maxStdDev * max(scale.x, max(scale.y, scale.z)) * focalPixelsY / clip.w;
    if (radiusPixels < minPixelRadius) return false;

    float2 ndc = clip.xy / clip.w;
    float2 marginNdc = radiusPixels * 2.0 / screenSize;
    return all(abs(ndc) <= 1.0 + marginNdc);
}

// Debug colour for chunk index visualisation: a cheap hash to a hue.
float3 GSplatChunkDebugColor(uint chunkIndex)
{
    float hue = frac(chunkIndex * 0.61803398875);
    float3 rgb = saturate(abs(frac(hue + float3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0) - 1.0);
    return rgb;
}

#endif
