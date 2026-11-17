using System;
using LargeFileSort.Sorter.Planning;

namespace LargeFileSort.Sorter.Models;

internal sealed class SortChunk(long budgetBytes)
{
    private SortRecord[] _records = [];
    private long _stringBytes;

    public long BudgetBytes { get; } = ValidateBudget(budgetBytes);

    public SortRecord[] Records => _records;

    public int Count { get; private set; }

    public int Capacity => Records.Length;

    public long EstimatedMemoryBytes { get; private set; }

    public long PeakEstimatedMemoryBytes { get; private set; }

    public bool TryAdd(SortRecord record)
    {
        long stringBytes = MemoryEstimator.StringBytes(record.Line.Length);
        if (Count == Array.MaxLength || stringBytes > BudgetBytes - EstimatedMemoryBytes)
        {
            return false;
        }

        if (Count == Capacity)
        {
            long available = BudgetBytes - EstimatedMemoryBytes - stringBytes;
            int maximum = MemoryEstimator.RecordArrayCapacity(available);
            if (maximum <= Count)
            {
                return false;
            }

            int desired = Capacity == 0 ? 4 : (int)Math.Min(2L * Capacity, Array.MaxLength);
            int capacity = Math.Min(desired, maximum);
            long arrayBytes = MemoryEstimator.RecordArrayBytes(capacity);
            long peakBytes = EstimatedMemoryBytes + stringBytes + arrayBytes;
            SortRecord[] expanded = new SortRecord[capacity];
            Array.Copy(_records, expanded, Count);
            _records = expanded;
            EstimatedMemoryBytes = _stringBytes + arrayBytes;
            PeakEstimatedMemoryBytes = Math.Max(PeakEstimatedMemoryBytes, peakBytes);
        }

        _records[Count++] = record;
        _stringBytes += stringBytes;
        EstimatedMemoryBytes += stringBytes;
        PeakEstimatedMemoryBytes = Math.Max(PeakEstimatedMemoryBytes, EstimatedMemoryBytes);
        return true;
    }

    public void Release()
    {
        _records = [];
        Count = 0;
        _stringBytes = 0;
        EstimatedMemoryBytes = 0;
        PeakEstimatedMemoryBytes = 0;
    }

    private static long ValidateBudget(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);
        return bytes;
    }
}
