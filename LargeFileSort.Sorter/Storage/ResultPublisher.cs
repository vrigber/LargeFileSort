using System;
using System.IO;
using System.Threading.Tasks;
using LargeFileSort.Sorter.Abstractions;

namespace LargeFileSort.Sorter.Storage;

internal sealed class ResultPublisher(
    string outputPath,
    int bufferCharacters,
    Action<string> deleteFile = null) : IAsyncDisposable
{
    private readonly string _targetPath = ValidateTarget(outputPath, bufferCharacters);
    private readonly Action<string> _deleteFile = deleteFile ?? File.Delete;
    private Utf8OutputWriter _writer;
    private string _temporaryPath;
    private bool _created;
    private bool _published;
    private bool _disposed;

    public string TemporaryPath => _temporaryPath;

    public (long RecordCount, long ByteCount) GetCompletedCounts()
    {
        if (_writer is null || !_writer.IsCompleted)
            throw new InvalidOperationException("The result output has not been successfully completed.");
        return (_writer.RecordCount, new FileInfo(_temporaryPath).Length);
    }

    public IOutputWriter CreateWriter()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_created) throw new InvalidOperationException("The result writer has already been created.");
        string path = Path.Combine(Path.GetDirectoryName(_targetPath), ".largefilesort-" + Guid.NewGuid().ToString("N") + ".tmp");
        FileStream stream = new(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = 1,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });
        _temporaryPath = path;
        _created = true;
        try { return _writer = new(stream, bufferCharacters); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            try { stream.Dispose(); }
            catch (Exception cleanup) when (cleanup is not OutOfMemoryException)
            {
                throw new AggregateException("Opening result output and closing its stream failed.", exception, cleanup);
            }
            throw;
        }
    }

    public void Publish()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_published) return;
        if (_writer is null || !_writer.IsCompleted)
            throw new InvalidOperationException("Only a successfully completed and closed output can be published.");
        File.Move(_temporaryPath, _targetPath, overwrite: false);
        _published = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        Exception failure = null;
        try
        {
            if (_writer is not null) await _writer.DisposeAsync();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { failure = exception; }

        if (_created && !_published)
        {
            try
            {
                _deleteFile(_temporaryPath);
                _created = false;
            }
            catch (Exception cleanup) when (cleanup is not OutOfMemoryException)
            {
                failure = failure is null ? cleanup : new AggregateException("Result closing and cleanup failed.", failure, cleanup);
            }
        }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        _disposed = true;
    }

    private static string ValidateTarget(string path, int bufferCharacters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferCharacters);
        path = Path.GetFullPath(path);
        if (File.Exists(path) || Directory.Exists(path)) throw new IOException($"Output path already exists: '{path}'.");
        string directory = Path.GetDirectoryName(path);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException($"Output parent directory does not exist: '{directory}'.");
        return path;
    }
}
