using System;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using LargeFileSort.Generator.Abstractions;
using LargeFileSort.Generator.Models;
using Microsoft.Extensions.Logging;

namespace LargeFileSort.Generator.Generation;

internal sealed class Generator
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    private readonly IPhraseSource _phrases;
    private readonly IOutputWriter _writer;
    private readonly GenerationOptions _options;
    private readonly ILogger<Generator> _logger;

    public Generator(IPhraseSource phrases, IOutputWriter writer, GenerationOptions options, ILogger<Generator> logger)
    {
        ArgumentNullException.ThrowIfNull(phrases);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxOutputBytes);
        ArgumentNullException.ThrowIfNull(logger);
        if (phrases.Count <= 0)
        {
            throw new ArgumentException("Phrase source must contain at least one phrase.", nameof(phrases));
        }

        _phrases = phrases;
        _writer = writer;
        _options = options;
        _logger = logger;
    }

    public async Task<GenerationResult> GenerateAsync()
    {
        GenerationOptions options = _options;
        var phraseRandom = new Random(options.Seed);
        var numberRandom = new Random(GetNumberSeed(options.Seed));
        var prefix = new char[12];
        long resetThreshold = options.MaxOutputBytes / 3 + (options.MaxOutputBytes % 3 == 0 ? 0 : 1);
        long acceptedBytes = 0;
        long acceptedLines = 0;
        bool seedReset = false;
        bool repeatConfirmed = false;

        _logger.LogInformation("Generating file with seed {Seed} and limit {MaxOutputBytes} bytes.",
            options.Seed, options.MaxOutputBytes);

        while (true)
        {
            int number = numberRandom.Next();
            string phrase = _phrases[phraseRandom.Next(_phrases.Count)];
            number.TryFormat(prefix.AsSpan(), out int prefixLength, provider: CultureInfo.InvariantCulture);
            prefix[prefixLength++] = '.';
            prefix[prefixLength++] = ' ';

            long lineBytes = prefixLength + GetPhraseByteCount(phrase) + 1;
            if (lineBytes > options.MaxOutputBytes - acceptedBytes)
            {
                break;
            }

            await _writer.WriteLineAsync(prefix.AsMemory(0, prefixLength), phrase.AsMemory()).ConfigureAwait(false);
            acceptedBytes += lineBytes;
            acceptedLines++;

            if (!seedReset && acceptedBytes >= resetThreshold)
            {
                await _writer.FlushAsync().ConfigureAwait(false);
                phraseRandom = new Random(options.Seed);
                seedReset = true;
                _logger.LogDebug("Phrase seed reset after {LineCount} lines and {ByteCount} bytes.",
                    acceptedLines, acceptedBytes);
            }
            else if (seedReset && !repeatConfirmed)
            {
                await _writer.FlushAsync().ConfigureAwait(false);
                repeatConfirmed = true;
            }
        }

        if (acceptedLines == 0)
        {
            throw new InvalidOperationException("The requested size cannot fit the first generated line.");
        }

        if (!repeatConfirmed)
        {
            throw new InvalidOperationException("The requested size did not allow a complete line to be confirmed after the phrase seed reset.");
        }

        await _writer.CompleteAsync().ConfigureAwait(false);
        _logger.LogInformation("Generation completed: {LineCount} lines, {ByteCount} bytes.", acceptedLines, acceptedBytes);
        return new GenerationResult(options.Seed, acceptedLines, acceptedBytes);
    }

    private static long GetPhraseByteCount(string phrase)
    {
        long byteCount = 0;
        int offset = 0;
        while (offset < phrase.Length)
        {
            int length = Math.Min(16_384, phrase.Length - offset);
            if (offset + length < phrase.Length && char.IsHighSurrogate(phrase[offset + length - 1]))
            {
                length--;
            }

            byteCount += Utf8.GetByteCount(phrase.AsSpan(offset, length));
            offset += length;
        }

        return byteCount;
    }

    private static int GetNumberSeed(int seed)
    {
        int result;
        unchecked
        {
            uint value = (uint)seed;
            value ^= value >> 16;
            value *= 0x85ebca6b;
            value ^= value >> 13;
            value *= 0xc2b2ae35;
            value ^= value >> 16;
            result = (int)value;
        }

        long normalizedSeed = seed == int.MinValue ? int.MaxValue : Math.Abs((long)seed);
        long normalizedResult = result == int.MinValue ? int.MaxValue : Math.Abs((long)result);
        return normalizedSeed == normalizedResult ? (normalizedSeed == 0 ? 1 : 0) : result;
    }
}
