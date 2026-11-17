using System;
using System.Threading;
using LargeFileSort.Sorter.Comparison;
using LargeFileSort.Sorter.Models;

namespace LargeFileSort.Sorter.Chunks;

internal sealed class ChunkSorter(SortRecordComparer comparer)
{
    private readonly SortRecordComparer _comparer = comparer ?? throw new ArgumentNullException(nameof(comparer));

    public void Sort(SortChunk chunk, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        cancellationToken.ThrowIfCancellationRequested();
        Array.Sort(chunk.Records, 0, chunk.Count, _comparer);
        cancellationToken.ThrowIfCancellationRequested();
    }
}
