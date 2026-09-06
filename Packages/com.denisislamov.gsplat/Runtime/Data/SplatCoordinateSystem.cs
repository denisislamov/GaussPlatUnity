namespace GSplat
{
    /// <summary>
    /// Handedness / axis convention of the source file, named the SPZ way: the letters are the directions of
    /// +X, +Y, +Z (Right/Left, Up/Down, Forward/Back). Unity is RUF. Conversion = flipping the axes that differ.
    /// </summary>
    public enum SplatCoordinateSystem
    {
        /// <summary>+X right, +Y up, +Z forward. Unity itself: no conversion.</summary>
        Ruf = 0,

        /// <summary>+X right, +Y up, +Z back. OpenGL / three.js; the default the SPZ tools assume for .spz files.</summary>
        Rub = 1,

        /// <summary>+X right, +Y down, +Z forward. COLMAP / the original 3DGS .ply files.</summary>
        Rdf = 2,

        /// <summary>+X left, +Y up, +Z forward. glTF/GLB as written by the SPZ tools.</summary>
        Luf = 3,

        /// <summary>+X left, +Y down, +Z forward. The InnerTest worlds: a 180 degree turn about Z relative to Unity.</summary>
        Ldf = 4
    }
}
