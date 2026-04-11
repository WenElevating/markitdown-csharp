namespace MarkItDown.Core;

public sealed class ConverterRegistry
{
    private readonly IReadOnlyList<IConverter> _converters;

    internal ConverterRegistry(IReadOnlyList<IConverter> converters)
    {
        _converters = converters;
    }

    public IConverter? FindConverter(DocumentConversionRequest request)
    {
        return _converters.FirstOrDefault(c => c.CanConvert(request));
    }

    public IEnumerable<IConverter> GetAllConverters() => _converters;
}
