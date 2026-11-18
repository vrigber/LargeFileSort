namespace LargeFileSort.Generator;

using System;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using LargeFileSort.Generator.Configuration;
using LargeFileSort.Generator.Models;
using LargeFileSort.Generator.Output;
using LargeFileSort.Generator.PhraseSources;
using Microsoft.Extensions.Logging;

internal class Program
{
    static async Task<int> Main(string[] args)
    {
        try
        {
            GenerationResult result = null;
            Stopwatch dictionaryTime = null;
            Stopwatch generationTime = null;
            long actualBytes = 0;
            var command = CommandLineParser.CreateCommand(async options =>
            {
                var configuration = ConfigurationLoader.Load();
                using var configurationLifetime = configuration as IDisposable;
                var generatorConfiguration = ConfigurationLoader.ReadGeneratorConfiguration(configuration);
                using var loggerFactory = LoggingConfiguration.CreateFactory(configuration);
                var logger = loggerFactory.CreateLogger<Program>();
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

                var writer = new OutputWriter(options.OutputPath, options.MaxOutputBytes,
                    generatorConfiguration.WriterBufferCharacters, loggerFactory.CreateLogger<OutputWriter>(),
                    AppInfo.GetAvailableFreeSpace);
                try
                {
                    await using (writer)
                    {
                        var generator = new Generation.Generator(phrases, writer,
                            new GenerationOptions(options.MaxOutputBytes, seed),
                            loggerFactory.CreateLogger<Generation.Generator>());

                        generationTime = Stopwatch.StartNew();
                        result = await generator.GenerateAsync();
                    }
                    generationTime.Stop();

                    actualBytes = new FileInfo(options.OutputPath).Length;
                    if (actualBytes != result.ByteCount)
                    {
                        throw new IOException(
                            $"Output size mismatch: expected {result.ByteCount} bytes, found {actualBytes} bytes.");
                    }
                }
                catch (Exception exception)
                {
                    try
                    {
                        File.Delete(options.OutputPath);
                    }
                    catch (Exception cleanup)
                    {
                        throw new AggregateException("Generation failed; output cleanup also failed.", exception, cleanup);
                    }
                    throw;
                }

                return 0;
            });

            int exitCode = await command.Parse(args).InvokeAsync(new InvocationConfiguration
            {
                EnableDefaultExceptionHandler = false
            });

            if (exitCode == 0 && result is not null)
            {
                double seconds = generationTime.Elapsed.TotalSeconds;
                double megabytesPerSecond = seconds > 0 ? actualBytes / 1_000_000.0 / seconds : 0;
                Console.WriteLine(FormattableString.Invariant($"Lines: {result.LineCount}"));
                Console.WriteLine(FormattableString.Invariant($"Actual bytes: {actualBytes}"));
                Console.WriteLine(FormattableString.Invariant($"Dictionary loading: {dictionaryTime.Elapsed.TotalSeconds:F3} s"));
                Console.WriteLine(FormattableString.Invariant($"Generation: {seconds:F3} s"));
                Console.WriteLine(FormattableString.Invariant($"Average speed: {megabytesPerSecond:F3} MB/s"));
            }
            return exitCode;
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
}
