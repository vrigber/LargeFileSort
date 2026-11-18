using System;
using System.Globalization;
using LargeFileSort.Sorter.Models;
using Microsoft.Extensions.Configuration;

namespace LargeFileSort.Sorter.Configuration;

internal static class ConfigurationLoader
{
    public static IConfigurationRoot Load() =>
        new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .Build();

    public static SorterConfiguration ReadSorterConfiguration(IConfiguration configuration) => new(
        ReadMemoryBudget(configuration),
        ReadInt32(configuration, nameof(SorterConfiguration.MaxParallelism), 1),
        ReadInt32(configuration, nameof(SorterConfiguration.MaxMergeSources), 2),
        ReadInt32(configuration, nameof(SorterConfiguration.ReaderBufferBytes), 1),
        ReadInt32(configuration, nameof(SorterConfiguration.WriterBufferCharacters), 1));

    private static long ReadMemoryBudget(IConfiguration configuration)
    {
        string name = nameof(SorterConfiguration.MemoryBudgetBytes);
        if (!long.TryParse(configuration[name], NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
            || value <= 0)
        {
            throw new ArgumentException($"appsettings.json: {name} must be a positive Int64 value in bytes.");
        }

        return value;
    }

    private static int ReadInt32(IConfiguration configuration, string name, int minimum)
    {
        if (!int.TryParse(configuration[name], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            || value < minimum)
        {
            throw new ArgumentException($"appsettings.json: {name} must be an integer in [{minimum}, {int.MaxValue}].");
        }

        return value;
    }
}
