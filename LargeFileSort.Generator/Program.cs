namespace LargeFileSort.Generator;

using System.CommandLine;
using LargeFileSort.Generator.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

internal class Program
{
    static int Main(string[] args)
    {
        IConfigurationRoot? configuration = null;
        ILoggerFactory? loggerFactory = null;
        ILogger<Program>? logger = null;
        bool outOfMemory = false;
        int exitCode = 1;

        try
        {
            var command = CommandLineParser.CreateCommand(options =>
            {
                configuration = ConfigurationLoader.Load();
                var generatorConfiguration = ConfigurationLoader.ReadGeneratorConfiguration(configuration);
                loggerFactory = LoggingConfiguration.CreateFactory(configuration);
                logger = loggerFactory.CreateLogger<Program>();

                logger.LogInformation("Validating application parameters.");
                logger.LogDebug(
                    "Dictionary: {DictionaryPath}; output: {OutputPath}; maximum output bytes: {MaxOutputBytes}; " +
                    "seed: {Seed}; maximum dictionary bytes: {MaxDictionaryBytes}; writer buffer characters: {WriterBufferCharacters}.",
                    options.DictionaryPath, options.OutputPath, options.MaxOutputBytes, options.Seed,
                    generatorConfiguration.MaxDictionaryBytes, generatorConfiguration.WriterBufferCharacters);
                Console.WriteLine("Parameters validated. File generation is not implemented yet.");
                logger.LogInformation("Parameter validation completed.");
                return 0;
            });

            exitCode = command.Parse(args).Invoke(new InvocationConfiguration
            {
                EnableDefaultExceptionHandler = false
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

        return exitCode;
    }

    private static bool ReportFailure(ILogger? logger, Exception exception)
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

    private static void DisposeResource(IDisposable? resource, ref bool outOfMemory, ref int exitCode)
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
