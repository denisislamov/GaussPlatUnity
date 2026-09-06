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
            Texture2D<float4> _ChunkRanges;

            CBUFFER_START(GSplatPerRenderer)
                float _MaxStdDev;        // how far out (in standard deviations) the quad reaches; sqrt(8) desktop, sqrt(5) mobile
                float _Opacity;          // renderer-wide multiplier (crossfades)
                float _Brightness;
                int _ShDegree;           // 0..3, already capped by what the data has
                int _ShTexelsPerSplat;
                int _Antialiased;        // 1 = scene trained with mip-splatting: compensate alpha for the 0.3 px dilation
                int _SrgbInput;          // 1 = splat colors are sRGB and must become linear before blending (linear projects)
                int _DebugMode;          // 0 normal, 1 chunk colors, 2 overdraw heat, 3 ellipse outlines
                float _MinPixelRadius;   // splats whose own radius (before dilation) is below this are skipped
                float _Dilation;         // pixels^2 added to the 2D covariance diagonal; 0.3 is the 3DGS rasterizer's value, 0 = off
                float _MaxPixelRadius;   // splats reaching further than this are shrunk to it (Spark: 512); huge near-camera floaters otherwise veil the whole screen
                int _TriangleMode;       // P2: 1 = one triangle per splat (3 vertices) instead of a quad; the tiler sees half the primitives
                int _CheapGaussian;      // P9: 1 = polynomial falloff instead of exp
                int _ClipLowAlpha;       // P9: 1 = discard fragments under 1/255 alpha (saves blend bandwidth, may disable tiler fast paths)
            CBUFFER_END

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                // half precision: fewer bytes per vertex through the tiler, which is the bottleneck with many splats
                half2 local : TEXCOORD0;       // quad position in standard deviations
                half4 color : TEXCOORD1;       // rgb linear, a = opacity incl. compensation
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

            // The splat's color for this view: base color, the view-dependent SH term (in the object's frame),
            // brightness, the color-space conversion and the chunk debug view.
            float3 SplatColor(GSplatUnpacked s, uint splatIndex, uint chunkIndex, float3 positionWS, float3x3 objectToWorld)
            {
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
                return color;
            }

            Varyings Vertex(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
            {
                // 1. Load: the order texture says which splat this instance is; the packed texture holds the splat.
                uint slot = instanceId;
                uint splatIndex = GSplatTexelToUint(_Order.Load(uint3(slot % GSPLAT_TEXTURE_WIDTH, slot / GSPLAT_TEXTURE_WIDTH, 0)));
                GSplatUnpacked s = GSplatUnpack(GSplatLoadPacked(_Splats, splatIndex));
                uint chunkIndex = splatIndex / GSPLAT_CHUNK_SIZE;
                float3 positionOS = GSplatChunkPosition(_ChunkRanges, chunkIndex, s.position);

                // 2. Project the center. Unity view space looks down -Z: anything with z >= -near is behind the near plane.
                float3 positionWS = TransformObjectToWorld(positionOS);
                float3 positionVS = TransformWorldToView(positionWS);
                if (positionVS.z >= -_ProjectionParams.y) return Culled();

                float4 positionCS = TransformWViewToHClip(positionVS);
                float2 ndc = positionCS.xy / positionCS.w;

                // 3. Project the covariance and turn it into a quad (dilation, size clamp, sub-pixel cull).
                float3x3 objectToWorld = (float3x3)UNITY_MATRIX_M;
                float3x3 worldToView = (float3x3)UNITY_MATRIX_V;
                float3x3 covarianceWS = GSplatWorldCovariance(objectToWorld, s.rotation, s.scale);
                float2 focal = float2(UNITY_MATRIX_P._m00 * _ScreenParams.x * 0.5, UNITY_MATRIX_P._m11 * _ScreenParams.y * 0.5);
                float3 cov = GSplatProjectCovariance(covarianceWS, worldToView, positionVS, focal);
                GSplatFootprint fp = GSplatScreenFootprint(cov, _MaxStdDev, _MinPixelRadius, _Dilation, _MaxPixelRadius, _Antialiased != 0);
                if (!fp.visible) return Culled();

                // 4. Off screen only when the whole quad is: big background splats often have their center outside the view.
                if (any(abs(ndc) > 1.0 + fp.radiusMajor * 2.0 / _ScreenParams.xy)) return Culled();

                // 5. This vertex's corner. Pixels to clip space: NDC spans 2 units over the screen, and clip = NDC * w.
                float2 corner = _TriangleMode != 0 ? GSplatTriangleCorner(vertexId) : GSplatQuadCorner(vertexId);
                positionCS.xy += GSplatCornerOffsetPixels(corner, fp) * (2.0 / _ScreenParams.xy) * positionCS.w;

                // 6. Color and opacity.
                Varyings o;
                o.positionCS = positionCS;
                o.local = (half2)(corner * _MaxStdDev);
                o.color = (half4)float4(SplatColor(s, splatIndex, chunkIndex, positionWS, objectToWorld), s.alpha * fp.compensation * _Opacity);
                return o;
            }

            float4 Fragment(Varyings i) : SV_Target
            {
                float distanceSq = dot(i.local, i.local);
                // In triangle mode the primitive reaches out to 2x the quad's half-size; cut it back to the quad's square so
                // both modes shade exactly the same pixels (the Gaussian tail beyond the square stays invisible as before).
                if (_TriangleMode != 0 && any(abs(i.local) > _MaxStdDev)) discard;
                float gaussian = _CheapGaussian != 0 ? GSplatCheapGaussian(distanceSq) : exp(-0.5 * distanceSq);
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
                // Switchable (P9) so the Mali question can be measured instead of guessed: does the clip cost more than it saves?
                if (_ClipLowAlpha != 0) clip(alpha - 1.0 / 255.0);
                return float4(i.color.rgb * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
