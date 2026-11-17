namespace LargeFileSort.Sorter;

using System;
using System.CommandLine;
using System.Threading.Tasks;
using LargeFileSort.Sorter.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        IConfigurationRoot configuration = null;
        ILoggerFactory loggerFactory = null;
        ILogger<Program> logger = null;
        bool outOfMemory = false;
        int exitCode = 1;

        try
        {
            var command = CommandLineParser.CreateCommand(options =>
            {
                configuration = ConfigurationLoader.Load();
                var settings = ConfigurationLoader.ReadSorterConfiguration(configuration);
                loggerFactory = LoggingConfiguration.CreateFactory(configuration);
                logger = loggerFactory.CreateLogger<Program>();

                logger.LogDebug(
                    "Input: {InputPath}; output: {OutputPath}; temporary directory: {TempDirectoryPath}; " +
                    "memory budget bytes: {MemoryBudgetBytes}; maximum parallelism: {MaxParallelism}; " +
                    "maximum merge sources: {MaxMergeSources}; extra disk space: {ExtraDiskSpace}; " +
                    "reader buffer bytes: {ReaderBufferBytes}; writer buffer characters: {WriterBufferCharacters}.",
                    options.InputPath, options.OutputPath, options.TempDirectoryPath,
                    settings.MemoryBudgetBytes, settings.MaxParallelism, settings.MaxMergeSources,
                    settings.ExtraDiskSpace, settings.ReaderBufferBytes, settings.WriterBufferCharacters);
                logger.LogInformation("Configuration loaded.");

                Console.Error.WriteLine("Sorter is not implemented yet.");
                return Task.FromResult(1);
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
            DisposeResource(loggerFactory, ref outOfMemory, ref exitCode);
            DisposeResource(configuration as IDisposable, ref outOfMemory, ref exitCode);
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
