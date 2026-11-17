namespace LargeFileSort.Sorter.Models;

internal sealed record SortedRun(
    long Id,
    long SegmentCount,
    long RecordCount,
    long ByteCount,
    int MaxLineCharacters,
    long MaxRecordBytes,
    long MaxSegmentBytes);
