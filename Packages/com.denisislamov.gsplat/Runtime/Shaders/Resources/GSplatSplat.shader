// Draws the splats of one GaussianSplatRenderer as camera-facing quads, back to front, with premultiplied alpha.
// One instanced procedural draw: instance = slot in the order texture, 4 vertices per instance (a 6-index quad).
// Target 3.5 on purpose: no structured buffers anywhere, so the same shader runs on WebGL2 / GLES 3.0. All the
// per-splat data comes from textures (see PackedSplat.cs and ISplatSorter.cs for why).
Shader "GSplat/Splat"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "GSplat"
            // Premultiplied alpha: the fragment outputs (rgb * a, a). ZTest against URP's opaque depth keeps splats
            // behind walls hidden; ZWrite off because a splat has no single depth.
            Blend One OneMinusSrcAlpha
            BlendOp Add
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vertex
            #pragma fragment Fragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "../GSplatCore.hlsl"

            Texture2D<float4> _Splats;
            Texture2D<float4> _Order;
            Texture2D<float4> _Sh;
            Texture2D<float4> _ChunkCenters;

            CBUFFER_START(GSplatPerRenderer)
                float _MaxStdDev;        // how far out (in standard deviations) the quad reaches; sqrt(8) desktop, sqrt(5) mobile
                float _Opacity;          // renderer-wide multiplier (crossfades)
                float _Brightness;
                int _ShDegree;           // 0..3, already capped by what the data has
                int _ShTexelsPerSplat;
                int _Antialiased;        // 1 = scene trained with mip-splatting: compensate alpha for the 0.3 px dilation
                int _SrgbInput;          // 1 = splat colors are sRGB and must become linear before blending (linear projects)
                int _DebugMode;          // 0 normal, 1 chunk colors, 2 overdraw heat, 3 ellipse outlines
                float _MinPixelRadius;   // splats smaller than this are skipped (sub-pixel cull)
            CBUFFER_END

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 local : TEXCOORD0;      // quad position in standard deviations
                float4 color : TEXCOORD1;      // rgb linear, a = opacity incl. compensation
            };

            // A quad no rasterizer will draw: all four corners at the same point behind the camera.
            Varyings Culled()
            {
                Varyings o;
                o.positionCS = float4(0.0, 0.0, 2.0, 1.0);
                o.local = 0.0;
                o.color = 0.0;
                return o;
            }

            Varyings Vertex(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
            {
                uint slot = instanceId;
                uint splatIndex = GSplatTexelToUint(_Order.Load(uint3(slot % GSPLAT_TEXTURE_WIDTH, slot / GSPLAT_TEXTURE_WIDTH, 0)));
                GSplatUnpacked s = GSplatUnpack(GSplatLoadPacked(_Splats, splatIndex));
                uint chunkIndex = splatIndex / GSPLAT_CHUNK_SIZE;
                float3 positionOS = s.position + _ChunkCenters.Load(uint3(chunkIndex, 0, 0)).xyz;

                float3 positionWS = TransformObjectToWorld(positionOS);
                float3 positionVS = TransformWorldToView(positionWS);
                // Unity view space looks down -Z: anything with z >= -near is behind the near plane.
                if (positionVS.z >= -_ProjectionParams.y) return Culled();

                float4 positionCS = TransformWViewToHClip(positionVS);
                float2 ndc = positionCS.xy / positionCS.w;
                if (any(abs(ndc) > 1.3)) return Culled(); // well outside the screen, even with a big radius

                float3x3 objectToWorld = (float3x3)UNITY_MATRIX_M;
                float3x3 worldToView = (float3x3)UNITY_MATRIX_V;
                float3x3 covarianceWS = GSplatWorldCovariance(objectToWorld, s.rotation, s.scale);
                float2 focal = float2(UNITY_MATRIX_P._m00 * _ScreenParams.x * 0.5, UNITY_MATRIX_P._m11 * _ScreenParams.y * 0.5);
                float3 cov = GSplatProjectCovariance(covarianceWS, worldToView, positionVS, focal);

                // Low-pass filter: every splat covers at least ~one pixel so thin ones do not flicker. With
                // mip-splatting data the opacity is scaled down to keep the total energy (Yu et al. 2023, eq. 7);
                // classic 3DGS data was trained with the dilation and no compensation, so we reproduce that.
                float detBefore = cov.x * cov.z - cov.y * cov.y;
                cov.x += 0.3;
                cov.z += 0.3;
                float detAfter = cov.x * cov.z - cov.y * cov.y;
                float compensation = _Antialiased != 0 ? sqrt(max(detBefore / detAfter, 0.0)) : 1.0;

                float2 majorAxis;
                float lambdaMajor, lambdaMinor;
                GSplatEllipseAxes(cov, majorAxis, lambdaMajor, lambdaMinor);
                float radiusMajor = _MaxStdDev * sqrt(lambdaMajor);
                float radiusMinor = _MaxStdDev * sqrt(lambdaMinor);
                if (radiusMajor < _MinPixelRadius) return Culled();

                float2 corner = float2((vertexId & 1u) != 0u ? 1.0 : -1.0, (vertexId & 2u) != 0u ? 1.0 : -1.0);
                float2 minorAxis = float2(-majorAxis.y, majorAxis.x);
                float2 offsetPixels = corner.x * majorAxis * radiusMajor + corner.y * minorAxis * radiusMinor;
                // Pixels to clip space: NDC spans 2 units over the screen, and clip = NDC * w.
                positionCS.xy += offsetPixels * (2.0 / _ScreenParams.xy) * positionCS.w;

                float3 color = s.color;
                if (_ShDegree > 0 && _ShTexelsPerSplat > 0)
                {
                    // SH coefficients live in the object's local frame; bring the view direction there (rotation only).
                    float3 dirWS = normalize(positionWS - _WorldSpaceCameraPos);
                    float3 dirOS = normalize(mul(transpose(objectToWorld), dirWS));
                    color += GSplatEvaluateSh(_Sh, splatIndex, (uint)_ShTexelsPerSplat, (uint)_ShDegree, dirOS);
                    color = saturate(color);
                }

                color *= _Brightness;
                if (_SrgbInput != 0)
                {
                    // Splat colors are trained against sRGB images. In a linear-color project the blending happens in
                    // linear space, so convert first; this is choice (a) of TZ E3-T5, the toggle exists for the A/B test.
                    color = SRGBToLinear(color);
                }

                if (_DebugMode == 1) color = GSplatChunkDebugColor(chunkIndex);

                Varyings o;
                o.positionCS = positionCS;
                o.local = corner * _MaxStdDev;
                o.color = float4(color, s.alpha * compensation * _Opacity);
                return o;
            }

            float4 Fragment(Varyings i) : SV_Target
            {
                float distanceSq = dot(i.local, i.local);
                float gaussian = exp(-0.5 * distanceSq);
                float alpha = i.color.a * gaussian;

                if (_DebugMode == 2)
                {
                    // Overdraw heat: additive constant per fragment (alpha 0 makes the blend purely additive).
                    return float4(0.03, 0.012, 0.0, 0.0);
                }

                if (_DebugMode == 3)
                {
                    float edge = _MaxStdDev * _MaxStdDev;
                    float ring = abs(distanceSq - edge * 0.85) < edge * 0.06 ? 1.0 : 0.0;
                    return float4(ring, ring, ring, ring);
                }

                // Below 1/255 the fragment could not change an 8-bit target; skipping it saves blend bandwidth.
                // TODO: measure on Mali whether this clip costs more than it saves (it disables some TBDR fast paths).
                clip(alpha - 1.0 / 255.0);
                return float4(i.color.rgb * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
