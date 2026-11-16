namespace LargeFileSort.Generator;

using System;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using LargeFileSort.Generator.Configuration;
using LargeFileSort.Generator.Abstractions;
using LargeFileSort.Generator.Models;
using LargeFileSort.Generator.Output;
using LargeFileSort.Generator.PhraseSources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

internal class Program
{
    static async Task<int> Main(string[] args)
    {
        IConfigurationRoot configuration = null;
        ILoggerFactory loggerFactory = null;
        ILogger<Program> logger = null;
        IOutputWriter writer = null;
        GenerationResult result = null;
        Stopwatch dictionaryTime = null;
        Stopwatch generationTime = null;
        string outputPath = null;
        long actualBytes = 0;
        bool ownsOutput = false;
        bool outputCompleted = false;
        bool outOfMemory = false;
        int exitCode = 1;

        try
        {
            var command = CommandLineParser.CreateCommand(async options =>
            {
                configuration = ConfigurationLoader.Load();
                var generatorConfiguration = ConfigurationLoader.ReadGeneratorConfiguration(configuration);
                loggerFactory = LoggingConfiguration.CreateFactory(configuration);
                logger = loggerFactory.CreateLogger<Program>();
                int seed = options.Seed ?? new Random().Next();

                logger.LogDebug(
                    "Dictionary: {DictionaryPath}; output: {OutputPath}; maximum output bytes: {MaxOutputBytes}; " +
                    "seed: {Seed}; maximum dictionary bytes: {MaxDictionaryBytes}; writer buffer characters: {WriterBufferCharacters}.",
                    options.DictionaryPath, options.OutputPath, options.MaxOutputBytes, seed,
                    generatorConfiguration.MaxDictionaryBytes, generatorConfiguration.WriterBufferCharacters);
                Console.WriteLine(FormattableString.Invariant($"Seed: {seed}"));

                dictionaryTime = Stopwatch.StartNew();
                var phrases = new MemoryPhraseSource(options.DictionaryPath, generatorConfiguration.MaxDictionaryBytes,
                    loggerFactory.CreateLogger<MemoryPhraseSource>());
                dictionaryTime.Stop();

                outputPath = options.OutputPath;
                writer = new OutputWriter(outputPath, options.MaxOutputBytes,
                    generatorConfiguration.WriterBufferCharacters, loggerFactory.CreateLogger<OutputWriter>(),
                    AppInfo.GetAvailableFreeSpace);
                ownsOutput = true;
                var generator = new Generation.Generator(phrases, writer,
                    new GenerationOptions(options.MaxOutputBytes, seed),
                    loggerFactory.CreateLogger<Generation.Generator>());

                generationTime = Stopwatch.StartNew();
                result = await generator.GenerateAsync().ConfigureAwait(false);
                await writer.DisposeAsync().ConfigureAwait(false);
                generationTime.Stop();

                actualBytes = new FileInfo(outputPath).Length;
                if (actualBytes != result.ByteCount)
                {
                    throw new IOException(
                        $"Output size mismatch: expected {result.ByteCount} bytes, found {actualBytes} bytes.");
                }

                outputCompleted = true;
                return 0;
            });

            exitCode = await command.Parse(args).InvokeAsync(new InvocationConfiguration
            {
                EnableDefaultExceptionHandler = false
            }).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            outOfMemory = ReportFailure(logger, exception);
            exitCode = 1;
        }
        finally
        {
            if (!outOfMemory && writer is not null)
            {
                try
                {
                    await writer.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    outOfMemory = ReportFailure(logger, exception);
                    exitCode = 1;
                }
            }

            if (!outOfMemory && ownsOutput && !outputCompleted)
            {
                try
                {
                    File.Delete(outputPath);
                }
                catch (Exception exception)
                {
                    outOfMemory = ReportFailure(null, exception);
                    exitCode = 1;
                }
            }

            DisposeResource(loggerFactory, ref outOfMemory, ref exitCode);
            DisposeResource(configuration as IDisposable, ref outOfMemory, ref exitCode);
        }

        if (exitCode == 0 && result is not null)
        {
            try
            {
                double seconds = generationTime.Elapsed.TotalSeconds;
                double megabytesPerSecond = seconds > 0 ? actualBytes / 1_000_000.0 / seconds : 0;
                Console.WriteLine(FormattableString.Invariant($"Lines: {result.LineCount}"));
                Console.WriteLine(FormattableString.Invariant($"Actual bytes: {actualBytes}"));
                Console.WriteLine(FormattableString.Invariant($"Dictionary loading: {dictionaryTime.Elapsed.TotalSeconds:F3} s"));
                Console.WriteLine(FormattableString.Invariant($"Generation: {seconds:F3} s"));
                Console.WriteLine(FormattableString.Invariant($"Average speed: {megabytesPerSecond:F3} MB/s"));
            }
            catch (Exception exception)
            {
                ReportFailure(null, exception);
                exitCode = 1;
            }
        }

        return exitCode;
    }

    private static bool ReportFailure(ILogger logger, Exception exception)
    {
        if (exception is OutOfMemoryException)
        {
            Console.Error.WriteLine("Not enough memory.");
            return true;
        }

        try
        {
            if (logger?.IsEnabled(LogLevel.Error) == true)
            {
                logger.LogError(exception, "Execution failed: {ErrorMessage}", exception.Message);
                return false;
            }
        }
        catch (OutOfMemoryException)
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
