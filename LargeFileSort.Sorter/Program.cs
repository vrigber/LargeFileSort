namespace LargeFileSort.Sorter;

using System;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LargeFileSort.Sorter.Application;
using LargeFileSort.Sorter.Chunks;
using LargeFileSort.Sorter.Comparison;
using LargeFileSort.Sorter.Configuration;
using LargeFileSort.Sorter.Merging;
using LargeFileSort.Sorter.Models;
using LargeFileSort.Sorter.Planning;
using LargeFileSort.Sorter.Reading;
using LargeFileSort.Sorter.Storage;
using Microsoft.Extensions.Logging;

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            SortResult result = null;
            var command = CommandLineParser.CreateCommand(async (options, cancellationToken) =>
            {
                var configuration = ConfigurationLoader.Load();
                using var configurationLifetime = configuration as IDisposable;
                var settings = ConfigurationLoader.ReadSorterConfiguration(configuration);
                using var loggerFactory = LoggingConfiguration.CreateFactory(configuration);
                var logger = loggerFactory.CreateLogger<Program>();

                logger.LogDebug(
                    "Input: {InputPath}; output: {OutputPath}; temporary directory: {TempDirectoryPath}; " +
                    "memory budget bytes: {MemoryBudgetBytes}; maximum parallelism: {MaxParallelism}; " +
                    "maximum merge sources: {MaxMergeSources}; " +
                    "reader buffer bytes: {ReaderBufferBytes}; writer buffer characters: {WriterBufferCharacters}.",
                    options.InputPath, options.OutputPath, options.TempDirectoryPath,
                    settings.MemoryBudgetBytes, settings.MaxParallelism, settings.MaxMergeSources,
                    settings.ReaderBufferBytes, settings.WriterBufferCharacters);
                logger.LogInformation("Configuration loaded.");

                result = await RunSortAsync(options, settings, loggerFactory, cancellationToken);
                return 0;
            });

            int exitCode = await command.Parse(args).InvokeAsync(new InvocationConfiguration
            {
                EnableDefaultExceptionHandler = false,
                ProcessTerminationTimeout = Timeout.InfiniteTimeSpan
            });

            if (exitCode == 0 && result is not null)
            {
                PrintResult(result);
            }
            return exitCode;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Sorting cancelled.");
            return 1;
        }
        catch (OutOfMemoryException)
        {
            Console.Error.WriteLine("Not enough memory.");
            return 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static async Task<SortResult> RunSortAsync(
        CommandLineOptions options, SorterConfiguration settings, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stopwatch preparation = Stopwatch.StartNew();
        SortPlan plan = SortPlanner.Create(settings, Environment.ProcessorCount);
        FileStream input = new(options.InputPath, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            BufferSize = 1,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });
        await using Utf8LineReader reader = new(input, options.InputPath, plan.ReaderBufferBytes);
        SortResult statistics = new() { InputByteCount = input.Length };
        await using RunStore store = new(options.TempDirectoryPath, plan);
        await using ResultPublisher publisher = new(options.OutputPath, plan.WriterBufferCharacters);
        SortRecordComparer comparer = new();
        ChunkBuilder builder = new(reader, options.InputPath, statistics, loggerFactory.CreateLogger<ChunkBuilder>());
        ChunkPipeline pipeline = new(builder, comparer, store, plan, publisher.CreateWriter, statistics,
            loggerFactory.CreateLogger<ChunkPipeline>());
        MergeCoordinator merger = new(new(comparer), store, plan, publisher, statistics,
            loggerFactory.CreateLogger<MergeCoordinator>());
        SortApplication application = new(pipeline, merger, publisher, statistics,
            loggerFactory.CreateLogger<SortApplication>());
        preparation.Stop();
        statistics.PreparationTime = preparation.Elapsed;
        return await application.RunAsync(cancellationToken);
    }

    private static void PrintResult(SortResult result)
    {
        Console.WriteLine(FormattableString.Invariant($"Input bytes: {result.InputByteCount}"));
        Console.WriteLine(FormattableString.Invariant($"Read lines: {result.ReadLineCount}"));
        Console.WriteLine(FormattableString.Invariant($"Skipped lines: {result.SkippedLineCount}"));
        Console.WriteLine(FormattableString.Invariant($"Written lines: {result.WrittenLineCount}"));
        Console.WriteLine(FormattableString.Invariant($"Written bytes: {result.WrittenByteCount}"));
        Console.WriteLine(FormattableString.Invariant($"Chunks: {result.ChunkCount}"));
        Console.WriteLine(FormattableString.Invariant($"Merge passes: {result.MergePassCount}"));
        Console.WriteLine(FormattableString.Invariant($"Merge groups: {result.MergeGroupCount}"));
        Console.WriteLine(FormattableString.Invariant($"Estimated peak RAM bytes: {result.PeakEstimatedMemoryBytes}"));
        Console.WriteLine(result.PeakWorkingSetBytes is long peak
            ? FormattableString.Invariant($"Process peak working set bytes: {peak}") : "Process peak working set bytes: unavailable");
        Console.WriteLine(FormattableString.Invariant($"Preparation: {result.PreparationTime.TotalSeconds:F3} s"));
        Console.WriteLine(FormattableString.Invariant($"Chunk phase: {result.ChunkPhaseTime.TotalSeconds:F3} s"));
        Console.WriteLine(FormattableString.Invariant($"Merge phase: {result.MergePhaseTime.TotalSeconds:F3} s"));
        Console.WriteLine(FormattableString.Invariant($"Publication: {result.PublicationTime.TotalSeconds:F3} s"));
        Console.WriteLine(FormattableString.Invariant($"Total: {result.TotalTime.TotalSeconds:F3} s"));
        double speed = result.TotalTime.TotalSeconds > 0 ? result.InputByteCount / 1_000_000.0 / result.TotalTime.TotalSeconds : 0;
        Console.WriteLine(FormattableString.Invariant($"Average speed: {speed:F3} MB/s (input)"));
    }
}
