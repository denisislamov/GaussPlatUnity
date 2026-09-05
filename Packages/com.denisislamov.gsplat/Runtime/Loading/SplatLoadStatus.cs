using System;

namespace GSplat
{
    public enum SplatLoadStage
    {
        Downloading,
        Decoding,
        Building,
        Ready,
        Failed
    }

    public enum SplatLoadError
    {
        None = 0,
        Cancelled,
        Network,
        NotFound,
        UnsupportedFormat,
        Corrupted,
        OutOfMemory,
        Unknown
    }

    /// <summary>What the UI shows while a world loads. Progress is 0..1 within the current stage.</summary>
    public readonly struct SplatLoadStatus
    {
        public readonly SplatLoadStage Stage;
        public readonly float Progress;
        public readonly string Message;

        public SplatLoadStatus(SplatLoadStage stage, float progress, string message)
        {
            Stage = stage;
            Progress = progress;
            Message = message;
        }

        public override string ToString()
        {
            return $"{Stage} {Progress:P0} {Message}";
        }
    }

    public sealed class SplatLoadException : Exception
    {
        public SplatLoadError Code { get; }

        public SplatLoadException(SplatLoadError code, string message) : base(message)
        {
            Code = code;
        }

        public SplatLoadException(SplatLoadError code, string message, Exception inner) : base(message, inner)
        {
            Code = code;
        }
    }
}
