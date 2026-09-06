using UnityEngine;

namespace GSplat
{
    /// <summary>One renderer's draw for one camera, prepared on the main thread and consumed by the render pass.</summary>
    public struct SplatDrawItem
    {
        /// <summary>Owns the order texture; when its DrawArgs is set the draw is indirect (GPU sorter).</summary>
        public ISplatSorter Sorter;
        public Matrix4x4 LocalToWorld;
        public MaterialPropertyBlock Properties;

        /// <summary>Instances to draw when the sorter has no DrawArgs (CPU sorter).</summary>
        public int InstanceCount;

        /// <summary>6 for a quad, 3 for the one-triangle mode (P2); picks the index buffer in the pass.</summary>
        public int IndexCount;

        /// <summary>Distance from the camera to the bounds center, to draw far objects first.</summary>
        public float DistanceToCamera;
    }
}
