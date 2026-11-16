using System.Globalization;
using LargeFileSort.Generator.Models;
using Microsoft.Extensions.Configuration;

namespace LargeFileSort.Generator.Configuration;

internal static class ConfigurationLoader
{
    public static GeneratorConfiguration Load()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .Build();
        using var configurationLifetime = configuration as IDisposable;

        string? dictionaryLimit = configuration[nameof(GeneratorConfiguration.MaxDictionaryBytes)];
        if (!long.TryParse(dictionaryLimit, NumberStyles.Integer, CultureInfo.InvariantCulture, out long maxDictionaryBytes)
            || maxDictionaryBytes <= 0)
        {
            throw new ArgumentException("appsettings.json: MaxDictionaryBytes must be a positive Int64 value in bytes.");
        }

        string? bufferValue = configuration[nameof(GeneratorConfiguration.WriterBufferCharacters)];
        int bufferCharacters = new GeneratorConfiguration(maxDictionaryBytes).WriterBufferCharacters;
        if (bufferValue is not null
            && (!int.TryParse(bufferValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out bufferCharacters)
                || bufferCharacters <= 0))
        {
            throw new ArgumentException("appsettings.json: WriterBufferCharacters must be a positive Int32 value in characters.");
        }

        if (bufferValue is null && configuration.GetChildren().Any(section =>
                string.Equals(section.Key, nameof(GeneratorConfiguration.WriterBufferCharacters), StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("appsettings.json: WriterBufferCharacters must be a positive Int32 value in characters.");
        }

        return new GeneratorConfiguration(maxDictionaryBytes, bufferCharacters);
    }
}
