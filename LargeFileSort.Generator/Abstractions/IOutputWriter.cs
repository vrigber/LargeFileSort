namespace LargeFileSort.Generator.Abstractions;

/// <summary>Writes character batches without adding separators or line endings.</summary>
/// <remarks>Calls must be sequential. Call CompleteAsync to finish the output.</remarks>
public interface IOutputWriter : IAsyncDisposable
{
    /// <summary>Writes a batch using caller-owned memory.</summary>
    /// <remarks>
    /// Keep the memory unchanged until completion; the writer does not retain it afterwards.
    /// </remarks>
    ValueTask WriteBatchAsync(ReadOnlyMemory<char> characters);

    /// <summary>Flushes buffered data and closes the output stream.</summary>
    ValueTask CompleteAsync();
}
