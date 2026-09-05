using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace GSplat
{
    /// <summary>
    /// Adds the splat pass to a URP renderer. Add it to the Universal Renderer Data asset (Editor: GSplat > Setup >
    /// Add Renderer Feature to URP Renderers). One feature draws every enabled <see cref="GaussianSplatRenderer"/>.
    /// </summary>
    [DisallowMultipleRendererFeature("Gaussian Splats")]
    public sealed class GaussianSplatRendererFeature : ScriptableRendererFeature
    {
        [SerializeField, Tooltip("After the skybox and before URP's transparent objects: splats sit on top of opaques with correct depth and under later transparents (TZ E5-T3 documents the limitation).")]
        private RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingSkybox;

        private GaussianSplatRenderPass pass;

        public override void Create()
        {
            pass = new GaussianSplatRenderPass { renderPassEvent = renderPassEvent };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (GaussianSplatRenderer.Active.Count == 0) return;

            CameraType cameraType = renderingData.cameraData.cameraType;
            if (cameraType == CameraType.Preview || cameraType == CameraType.Reflection) return;

            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing)
        {
            pass?.Dispose();
            pass = null;
        }
    }
}
