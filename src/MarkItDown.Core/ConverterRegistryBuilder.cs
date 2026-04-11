using System.Reflection;

namespace MarkItDown.Core;

public class ConverterRegistryBuilder
{
    private readonly List<IConverter> _converters = new();

    public ConverterRegistryBuilder Add(IConverter converter)
    {
        _converters.Add(converter);
        return this;
    }

    public ConverterRegistryBuilder Add(IEnumerable<IConverter> converters)
    {
        _converters.AddRange(converters);
        return this;
    }

    public ConverterRegistryBuilder AddFromAssembly(Assembly assembly)
    {
        var converterTypes = assembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsGenericTypeDefinition)
            .Where(t => typeof(IConverter).IsAssignableFrom(t) && t != typeof(BaseConverter));

        foreach (var type in converterTypes)
        {
            try
            {
                if (Activator.CreateInstance(type) is IConverter converter)
                {
                    _converters.Add(converter);
                }
            }
            catch (MissingMethodException)
            {
                // Skip types without parameterless constructors
            }
        }

        return this;
    }

    public ConverterRegistry Build()
    {
        var sorted = _converters
            .OrderBy(c => c.Priority)
            .ToList()
            .AsReadOnly();

        return new ConverterRegistry(sorted);
    }
}
