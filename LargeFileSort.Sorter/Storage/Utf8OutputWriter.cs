using System;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LargeFileSort.Sorter.Abstractions;

namespace LargeFileSort.Sorter.Storage;

internal sealed class Utf8OutputWriter(Stream stream, StorageAccount account, int bufferCharacters) : IOutputWriter
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly Stream _stream = ValidateStream(stream, bufferCharacters);
    private readonly StorageAccount _account = account ?? throw new ArgumentNullException(nameof(account));
    private StreamWriter _writer = CreateWriter(stream, account, bufferCharacters);
    private bool _closed;
    private bool _failed;

    public long ByteCount { get; private set; }
    public long RecordCount { get; private set; }
    public bool IsCompleted { get; private set; }

    public ValueTask WriteLineAsync(ReadOnlyMemory<char> line, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return WriteSizedLineAsync(line, GetLineByteCount(line.Span), cancellationToken);
        }
        catch { _failed = true; throw; }
    }

    internal async ValueTask WriteSizedLineAsync(ReadOnlyMemory<char> line, long byteCount, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _account.Reserve(byteCount);
            await _writer.WriteLineAsync(line, cancellationToken);
            ByteCount = checked(ByteCount + byteCount);
            RecordCount = checked(RecordCount + 1);
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

    /// <summary>Includes the appended LF and rejects invalid UTF-16.</summary>
    public static long GetLineByteCount(ReadOnlySpan<char> line)
    {
        long bytes = 1;
        while (!line.IsEmpty)
        {
            int count = Math.Min(16384, line.Length);
            if (count < line.Length && char.IsHighSurrogate(line[count - 1])) count--;
            bytes = checked(bytes + Utf8.GetByteCount(line[..count]));
            line = line[count..];
        }
        return bytes;
    }

    private async ValueTask CloseAsync(bool complete, CancellationToken cancellationToken)
    {
        if (_closed) return;
        _closed = true;
        Exception failure = null;
        long actualLength = 0;
        try
        {
            if (complete) await _writer.FlushAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { failure = exception; }
        try
        {
            actualLength = _stream.Length;
            if (complete && failure is null && actualLength != ByteCount)
                failure = new IOException($"Output size mismatch: expected {ByteCount} bytes, found {actualLength} bytes.");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { failure = Combine(failure, exception); }
        try { await _stream.DisposeAsync(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { failure = Combine(failure, exception); }
        finally { _writer = null; }
        _account.CloseWriter(_stream, actualLength);
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
        if (!stream.CanWrite || !stream.CanSeek || stream.Position != 0 || stream.Length != 0)
            throw new ArgumentException("Output stream must be writable, seekable and empty.", nameof(stream));
        return stream;
    }

    private static StreamWriter CreateWriter(Stream stream, StorageAccount account, int bufferCharacters)
    {
        StreamWriter writer = new(stream, Utf8, bufferCharacters, leaveOpen: true) { NewLine = "\n" };
        account.OpenWriter(stream);
        return writer;
    }

    private static Exception Combine(Exception first, Exception next) =>
        first is null ? next : new AggregateException("Output flushing or closing failed.", first, next);
}
