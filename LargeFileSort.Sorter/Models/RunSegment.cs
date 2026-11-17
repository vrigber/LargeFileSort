namespace LargeFileSort.Sorter.Models;

internal readonly struct RunSegment(long runId, long index, long byteCount)
{
    public long RunId { get; } = runId;

    public long Index { get; } = index;

    public long ByteCount { get; } = byteCount;
}
