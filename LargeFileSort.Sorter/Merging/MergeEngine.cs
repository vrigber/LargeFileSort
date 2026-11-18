using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LargeFileSort.Sorter.Abstractions;
using LargeFileSort.Sorter.Comparison;
using LargeFileSort.Sorter.Models;
using LargeFileSort.Sorter.Reading;

namespace LargeFileSort.Sorter.Merging;

internal sealed class MergeEngine(SortRecordComparer comparer)
{
    public async Task MergeAsync(IReadOnlyList<ILineReader> sources, IOutputWriter output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(output);
        cancellationToken.ThrowIfCancellationRequested();
        PriorityQueue<int, SortRecord> queue = new(sources.Count, comparer);
        string[] names = new string[sources.Count];

        async ValueTask ReadNextAsync(int index)
        {
            string line = await sources[index].ReadLineAsync(cancellationToken);
            if (line is not null)
                queue.Enqueue(index, RecordParser.Parse(line, names[index], sources[index].LineNumber));
        }

        try
        {
            for (int index = 0; index < sources.Count; index++)
            {
                names[index] = $"merge source {index + 1}";
                await ReadNextAsync(index);
            }
            while (queue.TryDequeue(out int source, out SortRecord record))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await output.WriteLineAsync(record.Line.AsMemory(), cancellationToken);
                record = default;
                await ReadNextAsync(source);
            }
        }
        finally { queue.Clear(); }
    }
}
