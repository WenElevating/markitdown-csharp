namespace MarkItDown.Core;

public sealed class UnsupportedFormatException : ConversionException
{
    public UnsupportedFormatException(string message) : base(message) { }
    public UnsupportedFormatException(string message, Exception innerException) : base(message, innerException) { }
}
