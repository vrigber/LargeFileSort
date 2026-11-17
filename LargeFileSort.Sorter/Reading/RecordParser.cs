using System;
using System.IO;
using LargeFileSort.Sorter.Models;

namespace LargeFileSort.Sorter.Reading;

internal sealed class RecordParser
{
    public static SortRecord Parse(string line, string source, long lineNumber)
    {
        ArgumentNullException.ThrowIfNull(line);

        int separator = line.IndexOf(". ", StringComparison.Ordinal);
        if (separator <= 0)
        {
            throw new InvalidDataException($"Source '{source}', line {lineNumber}: expected 'Number. String'.");
        }

        int number = 0;
        foreach (char character in line.AsSpan(0, separator))
        {
            int digit = character - '0';
            if ((uint)digit > 9)
            {
                throw new InvalidDataException($"Source '{source}', line {lineNumber}: the number must contain only ASCII digits.");
            }

            if (number > (int.MaxValue - 1 - digit) / 10)
            {
                throw new InvalidDataException($"Source '{source}', line {lineNumber}: the number must be in [0, {int.MaxValue}).");
            }

            number = number * 10 + digit;
        }

        int textOffset = separator + 2;
        foreach (char character in line.AsSpan(textOffset))
        {
            if (!char.IsWhiteSpace(character))
            {
                return new(line, number, textOffset);
            }
        }

        throw new InvalidDataException($"Source '{source}', line {lineNumber}: the text must contain a non-whitespace character.");
    }
}
