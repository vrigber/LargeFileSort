using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LargeFileSort.Sorter.Abstractions;

namespace LargeFileSort.Sorter.Reading;

internal sealed class Utf8LineReader(
    Stream stream,
    string source,
    int bufferSize,
    bool allowUtf8Bom = true) : ILineReader
{
    private readonly Stream _stream = ValidateStream(stream, source, bufferSize);
    private StreamReader _reader;
    private bool _failed;
    private bool _disposed;

    public long LineNumber { get; private set; }

    public async ValueTask<string> ReadLineAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_failed)
        {
            throw new InvalidOperationException("Reading cannot resume after an error or cancellation.");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_reader is null)
            {
                if (allowUtf8Bom)
                {
                    byte[] signature = new byte[3];
                    int count = await _stream.ReadAtLeastAsync(
                        signature, signature.Length, throwOnEndOfStream: false, cancellationToken);
                    _stream.Position = count == signature.Length && signature.AsSpan().SequenceEqual("\uFEFF"u8)
                        ? signature.Length
                        : 0;
                }

                _reader = new(_stream, new UTF8Encoding(false, true),
                    detectEncodingFromByteOrderMarks: false, bufferSize, leaveOpen: true);
            }

            string line = await _reader.ReadLineAsync(cancellationToken);
            if (line is not null)
            {
                LineNumber = checked(LineNumber + 1);
            }

            return line;
        }
        catch (DecoderFallbackException exception)
        {
            _failed = true;
            throw new InvalidDataException($"Invalid UTF-8 in source '{source}'.", exception);
        }
        catch
        {
            _failed = true;
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _reader?.Dispose();
        }
        finally
        {
            await _stream.DisposeAsync();
        }
    }

    private static Stream ValidateStream(Stream stream, string source, int bufferSize)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferSize);

        if (!stream.CanRead || !stream.CanSeek || stream.Position != 0)
        {
            throw new ArgumentException("The input stream must be readable, seekable and positioned at the start.", nameof(stream));
        }

        return stream;
    }
}
