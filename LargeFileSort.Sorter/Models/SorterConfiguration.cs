namespace LargeFileSort.Sorter.Models;

internal sealed record SorterConfiguration(
    long MemoryBudgetBytes,
    int MaxParallelism,
    int MaxMergeSources,
    int ExtraDiskSpace,
    int ReaderBufferBytes,
    int WriterBufferCharacters);
