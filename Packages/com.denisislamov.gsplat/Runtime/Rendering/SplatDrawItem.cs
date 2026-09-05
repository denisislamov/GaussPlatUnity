using UnityEngine;
using UnityEngine.Rendering;

namespace GSplat
{
    /// <summary>One renderer's draw for one camera, prepared on the main thread and consumed by the render pass.</summary>
    public struct SplatDrawItem
    {
        public GaussianSplatRenderer Renderer;
        public ISplatSorter Sorter;
        public Matrix4x4 LocalToWorld;
        public MaterialPropertyBlock Properties;

        /// <summary>Instances to draw when <see cref="DrawArgs"/> is null (CPU sorter).</summary>
        public int InstanceCount;

        /// <summary>Indirect arguments written by the GPU sorter; when set the draw is indirect and InstanceCount is only an upper bound.</summary>
        public ComputeBuffer DrawArgs;

        /// <summary>Distance from the camera to the bounds center, to draw far objects first.</summary>
        public float DistanceToCamera;
    }
}
