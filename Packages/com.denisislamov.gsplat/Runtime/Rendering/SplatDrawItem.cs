using UnityEngine;

namespace GSplat
{
    /// <summary>One renderer's draw for one camera, prepared on the main thread and consumed by the render pass.</summary>
    public struct SplatDrawItem
    {
        public GaussianSplatRenderer Renderer;
        public ISplatSorter Sorter;
        public Matrix4x4 LocalToWorld;
        public MaterialPropertyBlock Properties;
        public int InstanceCount;

        /// <summary>Distance from the camera to the bounds center, to draw far objects first.</summary>
        public float DistanceToCamera;
    }
}
