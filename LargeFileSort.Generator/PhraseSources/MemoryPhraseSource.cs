using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LargeFileSort.Generator.Abstractions;
using Microsoft.Extensions.Logging;

namespace LargeFileSort.Generator.PhraseSources;

internal sealed class MemoryPhraseSource : IPhraseSource
{
    private readonly List<string> _phrases;

    public MemoryPhraseSource(string dictionaryPath, long maxDictionaryBytes, ILogger<MemoryPhraseSource> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dictionaryPath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDictionaryBytes);
        ArgumentNullException.ThrowIfNull(logger);

        logger.LogInformation("Loading phrase dictionary: {DictionaryPath}.", dictionaryPath);

        using (var stream = new FileStream(dictionaryPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (stream.Length > maxDictionaryBytes)
            {
                throw new InvalidDataException(
                    $"Dictionary file size {stream.Length} bytes exceeds the configured limit of {maxDictionaryBytes} bytes.");
            }

            Span<byte> signature = stackalloc byte[3];
            int signatureLength = stream.ReadAtLeast(signature, signature.Length, throwOnEndOfStream: false);
            stream.Position = signatureLength == signature.Length && signature.SequenceEqual("\uFEFF"u8)
                ? signature.Length
                : 0;

            using var reader = new StreamReader(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: false);

            var phrases = new List<string>();
            while (reader.ReadLine() is { } line)
            {
                string phrase = line.Trim();
                if (phrase.Length > 0)
                {
                    phrases.Add(phrase);
                }
            }

            if (phrases.Count == 0)
            {
                throw new InvalidDataException("Dictionary contains no nonempty phrases after trimming whitespace.");
            }

            _phrases = phrases;
        }

        logger.LogInformation("Phrase dictionary loaded: {PhraseCount} phrases.", Count);
    }

    public int Count => _phrases.Count;

    public string this[int index] => _phrases[index];
}
