namespace GSplat
{
    /// <summary>Spherical-harmonics constants shared by the decoders, the packer and (later) the shaders.</summary>
    public static class ShMath
    {
        /// <summary>Highest SH degree this package keeps. SPZ can carry degree 4; we drop it (see SpzReader).</summary>
        public const int MaxDegree = 3;

        /// <summary>
        /// 1 / (2 * sqrt(pi)): the zeroth SH basis function. Display color = 0.5 + Sh0Scale * coefficient.
        /// Same constant as C0 in the 3DGS reference code and "colorScale" in the SPZ sources.
        /// </summary>
        public const float Sh0Scale = 0.28209479177387814f;

        /// <summary>
        /// Number of SH coefficients per color channel above degree 0: 0, 3, 8, 15 for degrees 0..3.
        /// (Degree 0 is stored separately as the color, which is why the count excludes it.)
        /// </summary>
        public static int CoefficientCount(int degree)
        {
            switch (degree)
            {
                case 0: return 0;
                case 1: return 3;
                case 2: return 8;
                case 3: return 15;
                case 4: return 24;
                default: return -1;
            }
        }

        /// <summary>Inverse of <see cref="CoefficientCount"/>; returns -1 for counts that match no degree.</summary>
        public static int DegreeForCoefficientCount(int count)
        {
            switch (count)
            {
                case 0: return 0;
                case 3: return 1;
                case 8: return 2;
                case 15: return 3;
                case 24: return 4;
                default: return -1;
            }
        }
    }
}
