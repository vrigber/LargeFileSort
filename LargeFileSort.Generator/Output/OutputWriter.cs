using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using LargeFileSort.Generator.Abstractions;
using Microsoft.Extensions.Logging;

namespace LargeFileSort.Generator.Output;

internal sealed class OutputWriter : IOutputWriter
{
    private readonly FileStream _stream;
    private readonly StreamWriter _writer;
    private bool _closed;
    private bool _completed;
    private bool _failed;

    public OutputWriter(
        string outputPath,
        long maxOutputBytes,
        int bufferCharacters,
        ILogger<OutputWriter> logger,
        Func<string, long> getAvailableFreeSpace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxOutputBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferCharacters);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(getAvailableFreeSpace);

        string fullPath = Path.GetFullPath(outputPath, AppInfo.CurrentDirectory);
        long? availableBytes = null;
        try
        {
            availableBytes = getAvailableFreeSpace(fullPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or Win32Exception or SecurityException or NotSupportedException or OverflowException or ArgumentException)
        {
            logger.LogWarning(exception,
                "Cannot determine available disk space for {OutputPath}; attempting to write the file.", fullPath);
        }

        if (availableBytes.HasValue && availableBytes.Value < maxOutputBytes)
        {
            throw new IOException(
                $"Not enough disk space: {availableBytes.Value} bytes available, {maxOutputBytes} bytes required.");
        }

        _stream = new FileStream(fullPath, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous
        });

        try
        {
            _writer = new StreamWriter(_stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                bufferCharacters, leaveOpen: true)
            {
                AutoFlush = false,
                NewLine = "\n"
            };
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            try
            {
                _stream.Dispose();
                File.Delete(fullPath);
            }
            catch (Exception cleanupException) when (cleanupException is not OutOfMemoryException)
            {
                throw new AggregateException("Output initialization and cleanup failed.", exception, cleanupException);
            }

            throw;
        }
    }

    public async ValueTask WriteLineAsync(ReadOnlyMemory<char> prefix, ReadOnlyMemory<char> phrase)
    {
        ThrowIfUnavailable();
        try
        {
            await _writer.WriteAsync(prefix).ConfigureAwait(false);
            await _writer.WriteLineAsync(phrase).ConfigureAwait(false);
        }
        catch
        {
            _failed = true;
            throw;
        }
    }

    public async ValueTask FlushAsync()
    {
        ThrowIfUnavailable();
        try
        {
            await _writer.FlushAsync().ConfigureAwait(false);
        }
        catch
        {
            _failed = true;
            throw;
        }
    }

    public async ValueTask CompleteAsync()
    {
        if (_completed)
        {
            return;
        }

        ThrowIfUnavailable();
        await CloseAsync(flush: true).ConfigureAwait(false);
        _completed = true;
    }

    public ValueTask DisposeAsync() => CloseAsync(flush: !_failed);

    private void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        if (_failed)
        {
            throw new InvalidOperationException("Output writing has failed and cannot continue.");
        }
    }

    private async ValueTask CloseAsync(bool flush)
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        Exception failure = null;
        try
        {
            if (flush)
            {
                await _writer.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (OutOfMemoryException)
        {
            _failed = true;
            throw;
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        try
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
        catch (OutOfMemoryException)
        {
            _failed = true;
            throw;
        }
        catch (Exception exception)
        {
            failure = failure is null
                ? exception
                : new AggregateException("Output flushing and closing failed.", failure, exception);
        }

        if (failure is not null)
        {
            _failed = true;
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
