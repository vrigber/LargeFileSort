using System;
using System.IO;
using System.Threading;

namespace LargeFileSort.Sorter.Storage;

internal sealed class StorageAccount(DiskBudget budget)
{
    private readonly DiskBudget _budget = budget ?? throw new ArgumentNullException(nameof(budget));
    private long _reserved;
    private long _closed;

    public void Reserve(long bytes)
    {
        _budget.Reserve(bytes);
        Interlocked.Add(ref _reserved, bytes);
    }

    public void OpenWriter(Stream stream) => _budget.OpenWriter(stream);

    public void CloseWriter(Stream stream, long length)
    {
        _budget.CloseWriter(stream, length);
        Interlocked.Add(ref _closed, length);
    }

    public void ReleaseDeletedFile(long bytes)
    {
        _budget.ReleaseDeleted(bytes, bytes);
        Interlocked.Add(ref _reserved, -bytes);
        Interlocked.Add(ref _closed, -bytes);
    }

    public void ReleaseDeletedOwner()
    {
        _budget.ReleaseDeleted(Interlocked.Read(ref _reserved), Interlocked.Read(ref _closed));
        _reserved = 0;
        _closed = 0;
    }
}
