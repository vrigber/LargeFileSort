using System;
using System.CommandLine;
using System.IO;
using System.Threading.Tasks;
using LargeFileSort.Sorter.Models;

namespace LargeFileSort.Sorter.Configuration;

internal static class CommandLineParser
{
    public static RootCommand CreateCommand(Func<CommandLineOptions, Task<int>> action)
    {
        Option<string> input = new("--input")
        {
            Description = "Input file path",
            Required = true
        };
        Option<string> output = new("--output")
        {
            Description = "New output file path",
            Required = true
        };
        Option<string> tempDirectory = new("--temp-directory")
        {
            Description = "Existing temporary data directory",
            Required = true
        };

        RootCommand command = new("Sort a UTF-8 file using external merge sorting.")
        {
            Options = { input, output, tempDirectory }
        };
        command.SetAction(async result =>
        {
            CommandLineOptions options = new(
                result.GetRequiredValue(input),
                result.GetRequiredValue(output),
                result.GetRequiredValue(tempDirectory));

            return await action(ValidatePaths(options));
        });

        return command;
    }

    private static CommandLineOptions ValidatePaths(CommandLineOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.InputPath)
            || string.IsNullOrWhiteSpace(options.OutputPath)
            || string.IsNullOrWhiteSpace(options.TempDirectoryPath))
        {
            throw new ArgumentException("Input, output and temporary directory paths must not be empty.");
        }

        string currentDirectory = Directory.GetCurrentDirectory();
        string inputPath = Path.GetFullPath(options.InputPath, currentDirectory);
        string outputPath = Path.GetFullPath(options.OutputPath, currentDirectory);
        string tempDirectoryPath = Path.GetFullPath(options.TempDirectoryPath, currentDirectory);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (string.Equals(inputPath, outputPath, comparison))
        {
            throw new ArgumentException("Input and output paths must be different.");
        }

        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException($"Input file does not exist: '{inputPath}'.", inputPath);
        }

        if (File.Exists(outputPath) || Directory.Exists(outputPath))
        {
            throw new IOException($"Output path already exists: '{outputPath}'.");
        }

        string outputDirectory = Path.GetDirectoryName(outputPath);
        if (!Directory.Exists(outputDirectory))
        {
            throw new DirectoryNotFoundException($"Output directory does not exist: '{outputDirectory}'.");
        }

        if (!Directory.Exists(tempDirectoryPath))
        {
            throw new DirectoryNotFoundException($"Temporary directory does not exist: '{tempDirectoryPath}'.");
        }

        return options with
        {
            InputPath = inputPath,
            OutputPath = outputPath,
            TempDirectoryPath = tempDirectoryPath
        };
    }
}
