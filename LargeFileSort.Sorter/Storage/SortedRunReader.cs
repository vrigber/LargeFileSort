using System;
using System.Threading;
using System.Threading.Tasks;
using LargeFileSort.Sorter.Abstractions;
using LargeFileSort.Sorter.Reading;

namespace LargeFileSort.Sorter.Storage;

internal sealed class SortedRunReader(RunStore store, long id, Utf8LineReader reader) : ILineReader
{
    private Utf8LineReader _reader = reader;
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
            if (_reader is null) return null;
            string line = await _reader.ReadLineAsync(cancellationToken);
            if (line is not null)
            {
                LineNumber++;
                return line;
            }
            await _reader.DisposeAsync();
            _reader = null;
            store.DeleteRun(id);
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
            if (_reader is not null) await _reader.DisposeAsync();
        }
        finally { _reader = null; }
    }
}
