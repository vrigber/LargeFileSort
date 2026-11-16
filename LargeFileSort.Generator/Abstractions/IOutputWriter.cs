using System;
using System.Threading.Tasks;

namespace LargeFileSort.Generator.Abstractions;

/// <summary>Writes a formatted prefix and phrase, appending LF.</summary>
/// <remarks>Calls must be sequential. Call CompleteAsync to finish the output.</remarks>
public interface IOutputWriter : IAsyncDisposable
{
    /// <summary>Accepts a line into the writing component, which may buffer it.</summary>
    /// <remarks>
    /// Keep the memory unchanged until completion; the writer does not retain it afterwards.
    /// </remarks>
    ValueTask WriteLineAsync(ReadOnlyMemory<char> prefix, ReadOnlyMemory<char> phrase);

    /// <summary>Flushes buffered data without closing the output stream.</summary>
    ValueTask FlushAsync();

    /// <summary>Flushes buffered data and closes the output stream.</summary>
    ValueTask CompleteAsync();
}
