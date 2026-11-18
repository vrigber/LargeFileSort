using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LargeFileSort.Sorter.Abstractions;
using LargeFileSort.Sorter.Models;
using LargeFileSort.Sorter.Planning;
using LargeFileSort.Sorter.Reading;
using Microsoft.Extensions.Logging;

namespace LargeFileSort.Sorter.Chunks;

internal sealed class ChunkBuilder(
    ILineReader reader,
    string source,
    SortResult statistics,
    ILogger<ChunkBuilder> logger)
{
    private (SortRecord Record, long LineNumber)? _pending;
    private bool _endOfInput;
    private bool _failed;

    public void Abort()
    {
        _pending = null;
        _failed = true;
    }

    public async ValueTask<bool> FillAsync(SortChunk chunk, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        if (_failed || chunk.Count != 0)
        {
            throw new InvalidOperationException("Filling requires an empty chunk and a builder without a prior failure.");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            while (_pending.HasValue || !_endOfInput)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (SortRecord Record, long LineNumber) current;
                if (_pending.HasValue)
                {
                    current = _pending.Value;
                }
                else
                {
                    string line = await reader.ReadLineAsync(cancellationToken);
                    if (line is null)
                    {
                        _endOfInput = true;
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        statistics.SkippedLineCount = checked(statistics.SkippedLineCount + 1);
                        logger.LogWarning("Source {Source}, line {LineNumber}: skipping an empty or whitespace-only line.", source, reader.LineNumber);
                        continue;
                    }

                    current = (RecordParser.Parse(line, source, reader.LineNumber), reader.LineNumber);
                }

                if (!chunk.TryAdd(current.Record))
                {
                    long required = MemoryEstimator.StringBytes(current.Record.Line.Length) + MemoryEstimator.RecordArrayBytes(1);
                    if (required > chunk.BudgetBytes)
                    {
                        throw new InvalidDataException($"Source '{source}', line {current.LineNumber}: the record requires at least {required} estimated bytes, exceeding the empty chunk budget of {chunk.BudgetBytes} bytes.");
                    }

                    _pending = current;
                    break;
                }

                _pending = null;
            }

            if (chunk.Count > 0)
            {
                statistics.ChunkCount = checked(statistics.ChunkCount + 1);
            }

            return _endOfInput;
        }
        catch
        {
            _failed = true;
            _pending = null;
            throw;
        }
        finally { statistics.ReadLineCount = reader.LineNumber; }
    }
}
