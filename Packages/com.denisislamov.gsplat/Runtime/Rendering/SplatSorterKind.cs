namespace GSplat
{
    public enum SplatSorterKind
    {
        /// <summary>GPU when compute shaders exist, otherwise CPU.</summary>
        Auto = 0,
        Gpu = 1,
        Cpu = 2
    }

    public enum SplatDebugMode
    {
        None = 0,
        ChunkColors = 1,
        Overdraw = 2,
        EllipseOutlines = 3
    }
}
