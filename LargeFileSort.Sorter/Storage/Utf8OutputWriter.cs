using System;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LargeFileSort.Sorter.Abstractions;

namespace LargeFileSort.Sorter.Storage;

internal sealed class Utf8OutputWriter(Stream stream, int bufferCharacters) : IOutputWriter
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly Stream _stream = ValidateStream(stream, bufferCharacters);
    private StreamWriter _writer = new(stream, Utf8, bufferCharacters, leaveOpen: true) { NewLine = "\n" };
    private bool _closed;
    private bool _failed;

    public long RecordCount { get; private set; }
    public bool IsCompleted { get; private set; }

    public async ValueTask WriteLineAsync(ReadOnlyMemory<char> line, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _writer.WriteLineAsync(line, cancellationToken);
            RecordCount++;
        }
        catch { _failed = true; throw; }
    }

    public async ValueTask CompleteAsync(CancellationToken cancellationToken)
    {
        if (IsCompleted) return;
        ThrowIfUnavailable();
        await CloseAsync(complete: true, cancellationToken);
        IsCompleted = true;
    }

    public ValueTask DisposeAsync() => CloseAsync(complete: false, CancellationToken.None);

    private async ValueTask CloseAsync(bool complete, CancellationToken cancellationToken)
    {
        if (_closed) return;
        _closed = true;
        Exception failure = null;
        try
        {
            if (complete) await _writer.FlushAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { failure = exception; }
        try { await _stream.DisposeAsync(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { failure = Combine(failure, exception); }
        finally { _writer = null; }
        if (failure is not null)
        {
            _failed = true;
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        if (_failed) throw new InvalidOperationException("Output writing has failed and cannot continue.");
    }

    private static Stream ValidateStream(Stream stream, int bufferCharacters)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferCharacters);
        if (!stream.CanWrite)
            throw new ArgumentException("Output stream must be writable.", nameof(stream));
        return stream;
    }

    private static Exception Combine(Exception first, Exception next) =>
        first is null ? next : new AggregateException("Output flushing or closing failed.", first, next);
}
