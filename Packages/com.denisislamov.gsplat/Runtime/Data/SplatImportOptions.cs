using System;
using UnityEngine;

namespace GSplat
{
    /// <summary>
    /// How a source file becomes <see cref="GsplatData"/>. Serializable so the editor importers can show it
    /// as-is; the runtime loader takes the same object.
    /// </summary>
    [Serializable]
    public sealed class SplatImportOptions
    {
        [Tooltip("Axis convention of the source file. SPZ tools assume RUB (three.js), 3DGS .ply files are RDF. Wrong choice = mirrored scene.")]
        public SplatCoordinateSystem SourceCoordinateSystem = SplatCoordinateSystem.Rub;

        [Tooltip("Highest SH degree to keep. 0 = flat color only (mobile default); higher degrees cost 9/24/45 bytes per splat.")]
        [Range(0, ShMath.MaxDegree)]
        public int TargetShDegree = 0;

        [Tooltip("Splats with opacity below this are dropped at import: they are invisible but still cost overdraw. 1/255 removes only the truly transparent ones.")]
        [Range(0f, 0.2f)]
        public float PruneAlphaBelow = 1f / 255f;

        [Tooltip("Keep at most this many splats, dropping the least important (opacity x area) first. 0 = no limit.")]
        [Min(0)]
        public int MaxSplatCount = 0;

        [Tooltip("Reorder splats along a Morton curve so each 65k chunk is spatially compact. Needed for chunk culling and for float16 position precision. Turn off only to debug.")]
        public bool SpatialSort = true;

        public SplatImportOptions Clone()
        {
            return (SplatImportOptions)MemberwiseClone();
        }
    }
}
