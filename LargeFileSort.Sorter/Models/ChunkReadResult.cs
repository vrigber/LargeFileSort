namespace LargeFileSort.Sorter.Models;

internal readonly struct ChunkReadResult(bool endOfInput, bool hasPendingRecord)
{
    public bool EndOfInput { get; } = endOfInput;

    public bool HasPendingRecord { get; } = hasPendingRecord;
}
