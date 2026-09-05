using System;

namespace GSplat
{
    public enum SpzError
    {
        None = 0,
        EmptyPayload,
        BadMagic,
        UnsupportedVersion,
        UnsupportedShDegree,
        UnsupportedCompression,
        TruncatedPayload,
        CorruptedCompression,
        TooManyPoints,
        InvalidValue
    }

    public sealed class SpzException : Exception
    {
        public SpzError Code { get; }

        public SpzException(SpzError code, string message) : base(message)
        {
            Code = code;
        }

        public SpzException(SpzError code, string message, Exception inner) : base(message, inner)
        {
            Code = code;
        }
    }
}
