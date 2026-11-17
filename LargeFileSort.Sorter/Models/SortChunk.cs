using System;

namespace LargeFileSort.Sorter.Models;

internal sealed class SortChunk
{
    public SortRecord[] Records { get; set; } = [];

    public int Count { get; set; }

    public int Capacity => Records.Length;

    public long EstimatedMemoryBytes { get; set; }
}
