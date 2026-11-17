using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace LargeFileSort.Sorter.Storage;

internal sealed class DiskBudget(long limitBytes, int maxOpenWriters = 1)
{
    private readonly object _gate = new();
    private readonly HashSet<Stream> _writers = new(ReferenceEqualityComparer.Instance);
    private long _reserved;
    private long _peakReserved;
    private long _closedBytes;
    private long _peakActual;

    public long LimitBytes { get; } = ValidateLimit(limitBytes);
    public int MaxOpenWriters { get; } = ValidateWriters(maxOpenWriters);
    public long ReservedBytes => Interlocked.Read(ref _reserved);
    public long PeakReservedBytes => Interlocked.Read(ref _peakReserved);
    public long PeakActualBytes { get { lock (_gate) return _peakActual; } }

    public void Reserve(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        long current;
        long next;
        do
        {
            current = Interlocked.Read(ref _reserved);
            if (bytes > LimitBytes - current)
            {
                throw new IOException($"Disk budget exceeded: {bytes} bytes required, {LimitBytes - current} bytes available; limit {LimitBytes} bytes.");
            }
            next = current + bytes;
        }
        while (Interlocked.CompareExchange(ref _reserved, next, current) != current);

        long peak;
        do { peak = Interlocked.Read(ref _peakReserved); }
        while (next > peak && Interlocked.CompareExchange(ref _peakReserved, next, peak) != peak);
    }

    public void CaptureActualPeak()
    {
        lock (_gate)
        {
            long total = _closedBytes;
            foreach (Stream stream in _writers) total = checked(total + stream.Length);
            _peakActual = Math.Max(_peakActual, total);
        }
    }

    internal void OpenWriter(Stream stream)
    {
        lock (_gate)
        {
            if (_writers.Count >= MaxOpenWriters)
                throw new InvalidOperationException("The planned number of open output files has been reached.");
            if (!_writers.Add(stream)) throw new InvalidOperationException("Output stream is already registered.");
        }
    }

    internal void CloseWriter(Stream stream, long length)
    {
        lock (_gate)
        {
            if (!_writers.Remove(stream)) throw new InvalidOperationException("Output stream is not registered.");
            _closedBytes = checked(_closedBytes + length);
        }
    }

    internal void ReleaseDeleted(long reservedBytes, long actualBytes)
    {
        lock (_gate)
        {
            if (reservedBytes < 0 || actualBytes < 0 || reservedBytes > ReservedBytes || actualBytes > _closedBytes)
                throw new InvalidOperationException("Deleted file accounting is inconsistent.");
            _closedBytes -= actualBytes;
            Interlocked.Add(ref _reserved, -reservedBytes);
        }
    }

    private static long ValidateLimit(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        return value;
    }

    private static int ValidateWriters(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        return value;
    }
}
