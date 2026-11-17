using System;
using System.Threading;
using System.Threading.Tasks;

namespace LargeFileSort.Sorter.Abstractions;

/// <remarks>Calls must be sequential. Call CompleteAsync before treating the output as successful.</remarks>
public interface IOutputWriter : IAsyncDisposable
{
    /// <summary>Appends LF; keep the memory unchanged until completion. It is not retained afterwards.</summary>
    ValueTask WriteLineAsync(ReadOnlyMemory<char> line, CancellationToken cancellationToken);

    /// <summary>Flushes and closes the output. Disposal alone does not confirm successful output.</summary>
    ValueTask CompleteAsync(CancellationToken cancellationToken);
}
