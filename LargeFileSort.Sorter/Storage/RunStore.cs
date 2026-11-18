using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LargeFileSort.Sorter.Abstractions;
using LargeFileSort.Sorter.Models;
using LargeFileSort.Sorter.Reading;

namespace LargeFileSort.Sorter.Storage;

internal sealed class RunStore(
    string tempDirectory,
    SortPlan plan,
    Action<string> deleteFile = null) : IAsyncDisposable
{
    private readonly SortPlan _plan = plan;
    private readonly Action<string> _deleteFile = deleteFile ?? File.Delete;
    private readonly string _directory = CreateDirectory(tempDirectory);
    private long _lastRunId;
    private bool _closed;
    private bool _cleaned;

    public string DirectoryPath => _directory;

    public (SortedRun Run, Utf8OutputWriter Writer) CreateWriter(int maxLineCharacters)
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        long id = Interlocked.Increment(ref _lastRunId);
        return (new(id, maxLineCharacters), OpenWriter(id));
    }

    public ILineReader OpenReader(SortedRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        ObjectDisposedException.ThrowIf(_closed, this);
        return new SortedRunReader(this, run.Id, OpenRunReader(run.Id));
    }

    private Utf8OutputWriter OpenWriter(long runId)
    {
        FileStream stream = new(RunPath(runId), new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = 1,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });
        try { return new(stream, _plan.WriterBufferCharacters); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            try { stream.Dispose(); }
            catch (Exception cleanup) when (cleanup is not OutOfMemoryException)
            {
                throw new AggregateException("Opening the run output and closing its stream failed.", exception, cleanup);
            }
            throw;
        }
    }

    private Utf8LineReader OpenRunReader(long runId)
    {
        string path = RunPath(runId);
        FileStream stream = new(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            BufferSize = 1,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });
        try { return new(stream, path, _plan.ReaderBufferBytes, allowUtf8Bom: false); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            try { stream.Dispose(); }
            catch (Exception cleanup) when (cleanup is not OutOfMemoryException)
            {
                throw new AggregateException("Opening the run input and closing its stream failed.", exception, cleanup);
            }
            throw;
        }
    }

    internal void DeleteRun(long runId) => _deleteFile(RunPath(runId));

    public ValueTask DisposeAsync()
    {
        if (_cleaned) return default;
        _closed = true;
        Exception firstFailure = null;
        long failureCount = 0;
        foreach (string file in Directory.EnumerateFiles(_directory))
        {
            try { _deleteFile(file); }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                firstFailure ??= exception;
                failureCount++;
            }
        }
        if (firstFailure is not null)
            throw new AggregateException($"Temporary cleanup failed for {failureCount} files; the first failure is attached.", firstFailure);
        Directory.Delete(_directory);
        _cleaned = true;
        return default;
    }

    private string RunPath(long runId) =>
        Path.Combine(_directory, FormattableString.Invariant($"run-{runId}.tmp"));

    private static string CreateDirectory(string parent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parent);
        parent = Path.GetFullPath(parent);
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException($"Temporary parent directory does not exist: '{parent}'.");
        string path = Path.Combine(parent, "largefilesort-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
