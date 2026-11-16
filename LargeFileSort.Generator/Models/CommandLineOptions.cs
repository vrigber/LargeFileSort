namespace LargeFileSort.Generator.Models;

public sealed record CommandLineOptions(
    string DictionaryPath,
    string OutputPath,
    long MaxOutputBytes,
    int? Seed);
