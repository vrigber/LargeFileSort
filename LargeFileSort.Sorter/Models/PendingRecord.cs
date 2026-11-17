namespace LargeFileSort.Sorter.Models;

internal readonly struct PendingRecord(SortRecord record, long lineNumber)
{
    public SortRecord Record { get; } = record;

    public long LineNumber { get; } = lineNumber;
}
