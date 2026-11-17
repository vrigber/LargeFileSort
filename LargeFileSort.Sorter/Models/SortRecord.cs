namespace LargeFileSort.Sorter.Models;

internal readonly struct SortRecord
{
    public SortRecord(string line, int number, int textOffset)
    {
        Line = line;
        Number = number;
        TextOffset = textOffset;
    }

    public string Line { get; }

    public int Number { get; }

    public int TextOffset { get; }
}
