namespace MarkItDown.Core;

public sealed class ConversionException : Exception
{
    public ConversionException(ConversionErrorCode code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public ConversionErrorCode Code { get; }
}
