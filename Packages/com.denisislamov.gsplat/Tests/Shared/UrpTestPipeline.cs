using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GSplat.Tests
{
    /// <summary>
    /// Swaps in a private URP asset whose renderer has the splat feature, so render tests do not depend on the
    /// project's pipeline settings. Dispose restores the previous pipeline.
    /// </summary>
    public sealed class UrpTestPipeline : IDisposable
    {
        private readonly RenderPipelineAsset previousDefault;
        private readonly RenderPipelineAsset previousQuality;
        private readonly UniversalRendererData rendererData;
        private readonly UniversalRenderPipelineAsset pipeline;
        private readonly GaussianSplatRendererFeature feature;

        public UrpTestPipeline()
        {
            previousDefault = GraphicsSettings.defaultRenderPipeline;
            previousQuality = QualitySettings.renderPipeline;

            rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
            rendererData.name = "GSplat Test Renderer";
            feature = ScriptableObject.CreateInstance<GaussianSplatRendererFeature>();
            feature.name = "Gaussian Splats";
            rendererData.rendererFeatures.Add(feature);
            rendererData.SetDirty();

            pipeline = UniversalRenderPipelineAsset.Create(rendererData);
            pipeline.name = "GSplat Test Pipeline";
            pipeline.msaaSampleCount = 1;
            pipeline.supportsHDR = false;
            pipeline.renderScale = 1f;

            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
        }

        public void Dispose()
        {
            GraphicsSettings.defaultRenderPipeline = previousDefault;
            QualitySettings.renderPipeline = previousQuality;
            UnityEngine.Object.DestroyImmediate(pipeline);
            UnityEngine.Object.DestroyImmediate(feature);
            UnityEngine.Object.DestroyImmediate(rendererData);
        }
    }
}
