using System;
using System.Text;

namespace LargeFileSort.Sorter.Planning;

internal static class MemoryEstimator
{
    public const int ArrayHeaderBytes = 32;
    public const int RecordSlotBytes = 24;
    public const int QueueSlotBytes = 128;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static long StringBytes(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        return Align(32 + 2L * length);
    }

    public static long RecordArrayBytes(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        return capacity == 0 ? 0 : Align(ArrayHeaderBytes + (long)RecordSlotBytes * capacity);
    }

    public static int RecordArrayCapacity(long budgetBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(budgetBytes);
        return (int)Math.Clamp((budgetBytes - ArrayHeaderBytes) / RecordSlotBytes, 0, Array.MaxLength);
    }

    public static long ReaderBytes(int bufferBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferBytes);
        int size = Math.Max(128, bufferBytes);
        return Align(ArrayHeaderBytes + (long)size)
            + Align(ArrayHeaderBytes + 2L * Utf8.GetMaxCharCount(size)) + 1024;
    }

    public static long WriterBytes(int bufferCharacters)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferCharacters);
        int size = Math.Max(128, bufferCharacters);
        return Align(ArrayHeaderBytes + 2L * size)
            + Align(ArrayHeaderBytes + (long)Utf8.GetMaxByteCount(size)) + 1024;
    }

    private static long Align(long bytes) => checked(bytes + 7) & ~7L;
}
