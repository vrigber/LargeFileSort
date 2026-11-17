using System;
using System.Threading;
using System.Threading.Tasks;
using LargeFileSort.Sorter.Abstractions;
using LargeFileSort.Sorter.Models;

namespace LargeFileSort.Sorter.Storage;

internal sealed class SegmentedRunWriter(RunStore store, long id, long segmentTargetBytes) : IOutputWriter
{
    private Utf8OutputWriter _current;
    private long _segmentCount;
    private long _recordCount;
    private long _byteCount;
    private int _maxCharacters;
    private long _maxRecordBytes;
    private long _maxSegmentBytes;
    private SortedRun _completed;
    private bool _failed;
    private bool _disposed;

    public async ValueTask WriteLineAsync(ReadOnlyMemory<char> line, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            long bytes = Utf8OutputWriter.GetLineByteCount(line.Span);
            if (_current is not null && _current.ByteCount > 0
                && (_current.ByteCount >= segmentTargetBytes || bytes > segmentTargetBytes - _current.ByteCount))
            {
                await FinishSegmentAsync(cancellationToken);
            }
            if (_current is null)
            {
                _current = store.OpenSegmentWriter(id, _segmentCount);
                _segmentCount = checked(_segmentCount + 1);
            }
            await _current.WriteSizedLineAsync(line, bytes, cancellationToken);
            _recordCount = checked(_recordCount + 1);
            _byteCount = checked(_byteCount + bytes);
            _maxCharacters = Math.Max(_maxCharacters, line.Length);
            _maxRecordBytes = Math.Max(_maxRecordBytes, bytes);
        }
        catch { _failed = true; throw; }
    }

    public async ValueTask CompleteAsync(CancellationToken cancellationToken)
    {
        if (_completed is not null) return;
        ThrowIfUnavailable();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await FinishSegmentAsync(cancellationToken);
            _completed = new(id, _segmentCount, _recordCount, _byteCount, _maxCharacters, _maxRecordBytes, _maxSegmentBytes);
        }
        catch { _failed = true; throw; }
    }

    public SortedRun GetCompletedRun() => _completed
        ?? throw new InvalidOperationException("The sorted run has not been successfully completed.");

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_current is not null) await _current.DisposeAsync();
        }
        finally
        {
            _current = null;
            store.ReleaseWriter();
        }
    }

    private async ValueTask FinishSegmentAsync(CancellationToken cancellationToken)
    {
        if (_current is null) return;
        await _current.CompleteAsync(cancellationToken);
        _maxSegmentBytes = Math.Max(_maxSegmentBytes, _current.ByteCount);
        await _current.DisposeAsync();
        _current = null;
    }

    private void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_failed || _completed is not null)
            throw new InvalidOperationException("The run is completed or has failed and cannot accept records.");
    }
}
