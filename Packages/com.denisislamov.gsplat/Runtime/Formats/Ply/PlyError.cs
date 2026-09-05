using System;

namespace GSplat
{
    public enum PlyError
    {
        None = 0,
        EmptyPayload,
        BadMagic,
        MalformedHeader,
        UnsupportedFormat,
        MissingProperty,
        UnsupportedPropertyType,
        TruncatedPayload,
        TooManyPoints,
        InvalidValue
    }

    public sealed class PlyException : Exception
    {
        public PlyError Code { get; }

        public PlyException(PlyError code, string message) : base(message)
        {
            Code = code;
        }
    }
}
