namespace LargeFileSort.Generator.Models;

internal sealed record GeneratorConfiguration(
    long MaxDictionaryBytes,
    int WriterBufferCharacters = 65_536);
