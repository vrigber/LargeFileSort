using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using LargeFileSort.Sorter.Abstractions;
using LargeFileSort.Sorter.Models;
using LargeFileSort.Sorter.Reading;

namespace LargeFileSort.Sorter.Storage;

internal sealed class RunStore(
    string tempDirectory,
    SortPlan plan,
    DiskBudget budget,
    Action<string> deleteFile = null) : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly HashSet<long> _readers = [];
    private readonly SortPlan _plan = ValidatePlan(plan, budget);
    private readonly DiskBudget _budget = budget;
    private readonly StorageAccount _account = new(budget);
    private readonly Action<string> _deleteFile = deleteFile ?? File.Delete;
    private readonly string _directory = CreateDirectory(tempDirectory);
    private long _lastRunId;
    private int _writers;
    private bool _closed;
    private bool _cleaned;

    public string DirectoryPath => _directory;

    public SegmentedRunWriter CreateWriter(long segmentTargetBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(segmentTargetBytes);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_writers >= _plan.WorkerCount) throw new InvalidOperationException("The planned number of run writers has been reached.");
            long id = checked(_lastRunId + 1);
            SegmentedRunWriter writer = new(this, id, segmentTargetBytes);
            _lastRunId = id;
            _writers++;
            return writer;
        }
    }

    public ILineReader OpenReader(SortedRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (run.Id < 1 || run.Id > _lastRunId || run.SegmentCount < 0 || run.RecordCount < 0 || run.ByteCount < 0)
                throw new ArgumentException("Run descriptor does not belong to this store or is invalid.", nameof(run));
            if (_readers.Count >= _plan.MaxMergeSources || !_readers.Add(run.Id))
                throw new InvalidOperationException("The run is already open or the planned reader limit has been reached.");
            return new SortedRunReader(this, run);
        }
    }

    internal Utf8OutputWriter OpenSegmentWriter(long runId, long index)
    {
        FileStream stream = new(SegmentPath(runId, index), new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = 1,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });
        try { return new(stream, _account, _plan.WriterBufferCharacters); }
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

    internal (RunSegment Segment, Utf8LineReader Reader) OpenSegmentReader(long runId, long index)
    {
        string path = SegmentPath(runId, index);
        FileStream stream = new(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            BufferSize = 1,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });
        try { return (new(runId, index, stream.Length), new(stream, path, _plan.ReaderBufferBytes, allowUtf8Bom: false)); }
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

    internal void DeleteSegment(RunSegment segment)
    {
        _budget.CaptureActualPeak();
        _deleteFile(SegmentPath(segment.RunId, segment.Index));
        _account.ReleaseDeletedFile(segment.ByteCount);
    }

    internal void ReleaseWriter() { lock (_gate) _writers--; }
    internal void ReleaseReader(long id) { lock (_gate) _readers.Remove(id); }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_cleaned) return default;
            if (_writers != 0 || _readers.Count != 0)
                throw new InvalidOperationException("Close run readers and writers before disposing their store.");
            _closed = true;
        }

        _budget.CaptureActualPeak();
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
        _account.ReleaseDeletedOwner();
        _cleaned = true;
        return default;
    }

    private string SegmentPath(long runId, long index) =>
        Path.Combine(_directory, FormattableString.Invariant($"run-{runId}-{index}.tmp"));

    private static SortPlan ValidatePlan(SortPlan plan, DiskBudget budget)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(budget);
        if (plan.DiskBudgetBytes != budget.LimitBytes || plan.WorkerCount < 1 || plan.MaxMergeSources < 2)
            throw new ArgumentException("Storage plan and disk budget are inconsistent.", nameof(plan));
        return plan;
    }

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
