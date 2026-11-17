namespace LargeFileSort.Sorter.Models;

internal sealed record CommandLineOptions(
    string InputPath,
    string OutputPath,
    string TempDirectoryPath);
