namespace LargeFileSort.Sorter;

using System;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using LargeFileSort.Sorter.Abstractions;
using LargeFileSort.Sorter.Application;
using LargeFileSort.Sorter.Chunks;
using LargeFileSort.Sorter.Comparison;
using LargeFileSort.Sorter.Configuration;
using LargeFileSort.Sorter.Merging;
using LargeFileSort.Sorter.Models;
using LargeFileSort.Sorter.Planning;
using LargeFileSort.Sorter.Reading;
using LargeFileSort.Sorter.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        IConfigurationRoot configuration = null;
        ILoggerFactory loggerFactory = null;
        ILogger<Program> logger = null;
        SortResult result = null;
        bool outOfMemory = false;
        int exitCode = 1;

        try
        {
            var command = CommandLineParser.CreateCommand(async (options, cancellationToken) =>
            {
                configuration = ConfigurationLoader.Load();
                var settings = ConfigurationLoader.ReadSorterConfiguration(configuration);
                loggerFactory = LoggingConfiguration.CreateFactory(configuration);
                logger = loggerFactory.CreateLogger<Program>();

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

            exitCode = await command.Parse(args).InvokeAsync(new InvocationConfiguration
            {
                EnableDefaultExceptionHandler = false,
                ProcessTerminationTimeout = Timeout.InfiniteTimeSpan
            });
        }
        catch (Exception exception)
        {
            outOfMemory = ReportFailure(logger, exception);
            exitCode = 1;
        }
        finally
        {
            DisposeResource(loggerFactory, ref outOfMemory, ref exitCode);
            DisposeResource(configuration as IDisposable, ref outOfMemory, ref exitCode);
        }

        if (exitCode == 0 && result is not null)
        {
            try { PrintResult(result); }
            catch (Exception exception)
            {
                ReportFailure(null, exception);
                exitCode = 1;
            }
        }
        return exitCode;
    }

    internal static async Task<SortResult> RunSortAsync(
        CommandLineOptions options, SorterConfiguration settings, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        FileStream input = null;
        ILineReader reader = null;
        RunStore store = null;
        ResultPublisher publisher = null;
        SortResult result = null;
        Exception failure = null;

        async ValueTask CloseAsync(IAsyncDisposable resource)
        {
            if (resource is null || IsOutOfMemory(failure)) return;
            try { await resource.DisposeAsync(); }
            catch (Exception cleanup)
            {
                failure = failure is null || IsOutOfMemory(cleanup) ? cleanup
                    : new AggregateException("Sorting failed; resource cleanup also failed.", failure, cleanup);
            }
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Stopwatch preparation = Stopwatch.StartNew();
            input = new(options.InputPath, new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                BufferSize = 1,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            });
            SortPlan plan = SortPlanner.Create(settings, Environment.ProcessorCount);
            SortResult statistics = new() { InputByteCount = input.Length };
            reader = new Utf8LineReader(input, options.InputPath, plan.ReaderBufferBytes);
            store = new(options.TempDirectoryPath, plan);
            publisher = new(options.OutputPath, plan.WriterBufferCharacters);
            SortRecordComparer comparer = new();
            ChunkBuilder builder = new(reader, options.InputPath, statistics, loggerFactory.CreateLogger<ChunkBuilder>());
            ChunkPipeline pipeline = new(builder, comparer, store, plan, publisher.CreateWriter, statistics,
                loggerFactory.CreateLogger<ChunkPipeline>());
            MergeCoordinator merger = new(new(comparer), store, plan, publisher, statistics,
                loggerFactory.CreateLogger<MergeCoordinator>());
            SortApplication application = new(reader, pipeline, merger, store, publisher, statistics,
                loggerFactory.CreateLogger<SortApplication>());
            preparation.Stop();
            statistics.PreparationTime = preparation.Elapsed;
            result = await application.RunAsync(cancellationToken);
        }
        catch (Exception exception) { failure = exception; }
        finally
        {
            await CloseAsync(reader ?? (IAsyncDisposable)input);
            await CloseAsync(store);
            await CloseAsync(publisher);
        }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        return result;
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

    private static bool IsOutOfMemory(Exception exception)
    {
        if (exception is OutOfMemoryException) return true;
        if (exception is AggregateException aggregate)
        {
            foreach (Exception inner in aggregate.InnerExceptions)
                if (IsOutOfMemory(inner)) return true;
            return false;
        }
        return exception?.InnerException is Exception cause && IsOutOfMemory(cause);
    }

    private static bool ReportFailure(ILogger logger, Exception exception)
    {
        if (IsOutOfMemory(exception))
        {
            Console.Error.WriteLine("Not enough memory.");
            return true;
        }

        if (exception is OperationCanceledException)
        {
            Console.Error.WriteLine("Sorting cancelled.");
            return false;
        }
        try
        {
            if (logger?.IsEnabled(LogLevel.Error) == true)
            {
                logger.LogError(exception, "Execution failed: {ErrorMessage}", exception.Message);
                return false;
            }
        }
        catch (Exception loggingException) when (IsOutOfMemory(loggingException))
        {
            Console.Error.WriteLine("Not enough memory.");
            return true;
        }
        catch (Exception loggingException)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine($"Logging failed: {loggingException.Message}");
            return false;
        }

        Console.Error.WriteLine(exception.Message);
        return false;
    }

    private static void DisposeResource(IDisposable resource, ref bool outOfMemory, ref int exitCode)
    {
        if (outOfMemory)
        {
            return;
        }

        try
        {
            resource?.Dispose();
        }
        catch (Exception exception)
        {
            outOfMemory = ReportFailure(null, exception);
            exitCode = 1;
        }
    }
}
