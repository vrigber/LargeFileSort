namespace LargeFileSort.Generator.Models;

public sealed record GeneratorConfiguration(
    long MaxDictionaryBytes,
    int WriterBufferCharacters = 65_536);
