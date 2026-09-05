using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace GSplat
{
    /// <summary>
    /// The URP pass: for the current camera, prepares every active renderer (culling + sort request on the main
    /// thread), then records one compute pass (GPU sorters) and one raster pass that draws all renderers, far to
    /// near, into the camera color target with the camera depth bound for testing.
    /// </summary>
    public sealed class GaussianSplatRenderPass : ScriptableRenderPass, IDisposable
    {
        private const string ShaderResourceName = "GSplatSplat";

        private sealed class ComputePassData
        {
            public readonly List<SplatDrawItem> Items = new List<SplatDrawItem>();
        }

        private sealed class RasterPassData
        {
            public readonly List<SplatDrawItem> Items = new List<SplatDrawItem>();
            public Material Material;
            public GraphicsBuffer QuadIndices;
        }

        private static readonly Comparison<SplatDrawItem> FarToNear = (a, b) => b.DistanceToCamera.CompareTo(a.DistanceToCamera);

        private readonly List<SplatDrawItem> items = new List<SplatDrawItem>();
        private Material material;
        private GraphicsBuffer quadIndices;

        public GaussianSplatRenderPass()
        {
            profilingSampler = new ProfilingSampler("Gaussian Splats");
        }

        /// <summary>Loads the shader from Resources on first use; null (and a one-time error) if the package is broken.</summary>
        private Material GetMaterial()
        {
            if (material != null) return material;

            var shader = Resources.Load<Shader>(ShaderResourceName);
            if (shader == null)
            {
                Debug.LogError($"GSplat: shader Resources/{ShaderResourceName} was not found; splats cannot be drawn.");
                return null;
            }

            material = new Material(shader) { name = "GSplat Splat", hideFlags = HideFlags.HideAndDontSave };
            return material;
        }

        /// <summary>Six indices for a two-triangle quad; every instance reuses them (vertex 0..3 -> corner via SV_VertexID).</summary>
        private GraphicsBuffer GetQuadIndices()
        {
            if (quadIndices != null) return quadIndices;
            quadIndices = new GraphicsBuffer(GraphicsBuffer.Target.Index, 6, sizeof(ushort)) { name = "GSplat Quad Indices" };
            quadIndices.SetData(new ushort[] { 0, 1, 2, 2, 1, 3 });
            return quadIndices;
        }

        private bool PrepareItems(Camera camera)
        {
            items.Clear();
            IReadOnlyList<GaussianSplatRenderer> renderers = GaussianSplatRenderer.Active;
            for (int rendererIndex = 0; rendererIndex < renderers.Count; rendererIndex++)
            {
                if (renderers[rendererIndex].TryPrepare(camera, out SplatDrawItem item)) items.Add(item);
            }

            // Objects are alpha blended, so far objects first; splats inside one object are already sorted.
            items.Sort(FarToNear);
            return items.Count > 0;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            Material splatMaterial = GetMaterial();
            if (splatMaterial == null || !PrepareItems(cameraData.camera)) return;

            bool anyCompute = false;
            for (int itemIndex = 0; itemIndex < items.Count; itemIndex++) anyCompute |= items[itemIndex].Sorter.NeedsCompute;

            if (anyCompute)
            {
                // An "unsafe" pass because the sorters bind their own RenderTexture (not a graph resource) as a compute
                // target, which the graph's ComputeCommandBuffer cannot do. The graph also cannot see that the draw pass
                // reads that texture, so culling must be off and the two passes stay in recording order (they do:
                // RenderGraph never reorders). TODO: import the order RenderTexture as an RTHandle to make the
                // dependency explicit and unlock async compute.
                using (IUnsafeRenderGraphBuilder builder = renderGraph.AddUnsafePass("Gaussian Splats Sort", out ComputePassData passData, profilingSampler))
                {
                    passData.Items.Clear();
                    passData.Items.AddRange(items);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc<ComputePassData>(static (data, context) =>
                    {
                        CommandBuffer commands = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                        for (int itemIndex = 0; itemIndex < data.Items.Count; itemIndex++)
                        {
                            if (data.Items[itemIndex].Sorter.NeedsCompute) data.Items[itemIndex].Sorter.RecordCompute(commands);
                        }
                    });
                }
            }

            using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass("Gaussian Splats Draw", out RasterPassData passData, profilingSampler))
            {
                passData.Items.Clear();
                passData.Items.AddRange(items);
                passData.Material = splatMaterial;
                passData.QuadIndices = GetQuadIndices();
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Read);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc<RasterPassData>(static (data, context) =>
                {
                    for (int itemIndex = 0; itemIndex < data.Items.Count; itemIndex++)
                    {
                        SplatDrawItem item = data.Items[itemIndex];
                        context.cmd.DrawProcedural(data.QuadIndices, item.LocalToWorld, data.Material, 0, MeshTopology.Triangles, 6, item.InstanceCount, item.Properties);
                    }
                });
            }
        }

        /// <summary>Compatibility Mode (RenderGraph disabled in URP settings): same work on a plain command buffer.</summary>
        [Obsolete("Compatibility Mode path; URP calls it only when RenderGraph is disabled.", false)]
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            Material splatMaterial = GetMaterial();
            if (splatMaterial == null || !PrepareItems(renderingData.cameraData.camera)) return;

            CommandBuffer commands = CommandBufferPool.Get("Gaussian Splats");
            for (int itemIndex = 0; itemIndex < items.Count; itemIndex++)
            {
                if (items[itemIndex].Sorter.NeedsCompute) items[itemIndex].Sorter.RecordCompute(commands);
            }

            GraphicsBuffer indices = GetQuadIndices();
            for (int itemIndex = 0; itemIndex < items.Count; itemIndex++)
            {
                SplatDrawItem item = items[itemIndex];
                commands.DrawProcedural(indices, item.LocalToWorld, splatMaterial, 0, MeshTopology.Triangles, 6, item.InstanceCount, item.Properties);
            }

            context.ExecuteCommandBuffer(commands);
            CommandBufferPool.Release(commands);
        }

        public void Dispose()
        {
            if (material != null)
            {
                CoreUtils.Destroy(material);
                material = null;
            }

            quadIndices?.Dispose();
            quadIndices = null;
        }
    }
}
