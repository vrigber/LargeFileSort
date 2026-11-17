using System;
using System.Threading;
using System.Threading.Tasks;

namespace LargeFileSort.Sorter.Abstractions;

/// <remarks>Calls must be sequential. Do not resume reading after an error or cancellation.</remarks>
public interface ILineReader : IAsyncDisposable
{
    /// <summary>Counts returned lines from 1, including blank lines; EOF does not advance the count.</summary>
    long LineNumber { get; }

    /// <summary>Returns null at EOF; returned strings remain valid after subsequent reads.</summary>
    ValueTask<string> ReadLineAsync(CancellationToken cancellationToken);
}
