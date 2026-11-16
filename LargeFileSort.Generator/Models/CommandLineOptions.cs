namespace LargeFileSort.Generator.Models;

internal sealed record CommandLineOptions(
    string DictionaryPath,
    string OutputPath,
    long MaxOutputBytes,
    int? Seed);
