using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LargeFileSort.Sorter.Abstractions;
using LargeFileSort.Sorter.Models;
using LargeFileSort.Sorter.Reading;

namespace LargeFileSort.Sorter.Storage;

internal sealed class SortedRunReader(RunStore store, SortedRun run) : ILineReader
{
    private Utf8LineReader _current;
    private RunSegment _segment;
    private long _nextSegment;
    private long _byteCount;
    private bool _ended;
    private bool _failed;
    private bool _disposed;

    public long LineNumber { get; private set; }

    public async ValueTask<string> ReadLineAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_failed) throw new InvalidOperationException("Run reading cannot resume after an error or cancellation.");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            while (!_ended)
            {
                if (_current is null)
                {
                    if (_nextSegment == run.SegmentCount)
                    {
                        if (LineNumber != run.RecordCount || _byteCount != run.ByteCount)
                            throw new InvalidDataException($"Run {run.Id} size/count mismatch: expected {run.RecordCount} records and {run.ByteCount} bytes, read {LineNumber} records and {_byteCount} bytes.");
                        _ended = true;
                        return null;
                    }
                    (_segment, _current) = store.OpenSegmentReader(run.Id, _nextSegment);
                    _byteCount = checked(_byteCount + _segment.ByteCount);
                    if (_byteCount > run.ByteCount) throw new InvalidDataException($"Run {run.Id} exceeds its recorded byte count.");
                }

                string line = await _current.ReadLineAsync(cancellationToken);
                if (line is not null)
                {
                    LineNumber = checked(LineNumber + 1);
                    if (LineNumber > run.RecordCount) throw new InvalidDataException($"Run {run.Id} exceeds its recorded row count.");
                    return line;
                }
                await _current.DisposeAsync();
                _current = null;
                store.DeleteSegment(_segment);
                _nextSegment = checked(_nextSegment + 1);
            }
            return null;
        }
        catch { _failed = true; throw; }
    }

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
            store.ReleaseReader(run.Id);
        }
    }
}
