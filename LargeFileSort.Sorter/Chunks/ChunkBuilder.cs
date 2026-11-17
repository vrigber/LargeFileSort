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
    SortStatistics statistics,
    ILogger<ChunkBuilder> logger)
{
    private readonly ILineReader _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    private readonly string _source = !string.IsNullOrWhiteSpace(source) ? source : throw new ArgumentException("Source must not be empty.", nameof(source));
    private readonly SortStatistics _statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
    private readonly ILogger<ChunkBuilder> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private PendingRecord? _pending;
    private bool _endOfInput;
    private bool _failed;

    public async ValueTask<ChunkReadResult> FillAsync(SortChunk chunk, CancellationToken cancellationToken)
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
                PendingRecord current;
                if (_pending.HasValue)
                {
                    current = _pending.Value;
                }
                else
                {
                    string line = await _reader.ReadLineAsync(cancellationToken);
                    if (line is null)
                    {
                        _endOfInput = true;
                        break;
                    }

                    _statistics.ReadLineCount = checked(_statistics.ReadLineCount + 1);
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        _statistics.SkippedLineCount = checked(_statistics.SkippedLineCount + 1);
                        _logger.LogWarning("Source {Source}, line {LineNumber}: skipping an empty or whitespace-only line.", _source, _reader.LineNumber);
                        continue;
                    }

                    current = new(RecordParser.Parse(line, _source, _reader.LineNumber), _reader.LineNumber);
                }

                if (!chunk.TryAdd(current.Record))
                {
                    long required = MemoryEstimator.StringBytes(current.Record.Line.Length) + MemoryEstimator.RecordArrayBytes(1);
                    if (required > chunk.BudgetBytes)
                    {
                        throw new InvalidDataException($"Source '{_source}', line {current.LineNumber}: the record requires at least {required} estimated bytes, exceeding the empty chunk budget of {chunk.BudgetBytes} bytes.");
                    }

                    _pending = current;
                    break;
                }

                _pending = null;
            }

            if (chunk.Count > 0)
            {
                _statistics.ChunkCount = checked(_statistics.ChunkCount + 1);
            }

            return new(_endOfInput, _pending.HasValue);
        }
        catch
        {
            _failed = true;
            _pending = null;
            throw;
        }
    }
}
