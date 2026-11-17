using System;
using System.Collections.Generic;
using LargeFileSort.Sorter.Models;

namespace LargeFileSort.Sorter.Comparison;

internal sealed class SortRecordComparer : IComparer<SortRecord>
{
    public int Compare(SortRecord x, SortRecord y)
    {
        int textComparison = x.Line.AsSpan(x.TextOffset).CompareTo(
            y.Line.AsSpan(y.TextOffset), StringComparison.OrdinalIgnoreCase);

        return textComparison != 0 ? textComparison : x.Number.CompareTo(y.Number);
    }
}
