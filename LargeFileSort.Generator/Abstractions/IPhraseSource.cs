namespace LargeFileSort.Generator.Abstractions;

/// <summary>Provides indexed access to normalized, nonempty phrases.</summary>
public interface IPhraseSource
{
    int Count { get; }

    string this[int index] { get; }
}
