namespace GSplat
{
    /// <summary>Where the viewer is with the current world; the UI maps these to text (TZ E8-T4).</summary>
    public enum WorldLoadState
    {
        Idle,
        LoadingDescriptor,
        LoadingFirstLevel,
        ShowingFirstLevel,
        LoadingFinalLevel,
        Crossfading,
        Ready,
        Failed
    }
}
