using System.CommandLine;
using LargeFileSort.Generator.Models;

namespace LargeFileSort.Generator.Configuration;

internal static class CommandLineParser
{
    public static RootCommand CreateCommand(Func<CommandLineOptions, int> action)
    {
        var dictionary = new Option<string>("--dictionary")
        {
            Description = "Dictionary file path, relative to the current working directory.",
            Required = true
        };
        var output = new Option<string>("--output")
        {
            Description = "New output file path, relative to the current working directory.",
            Required = true
        };
        var maxBytes = new Option<long>("--max-bytes")
        {
            Description = "Positive output size limit in bytes.",
            Required = true
        };
        var seed = new Option<int?>("--seed")
        {
            Description = "Optional Int32 seed."
        };

        maxBytes.Validators.Add(result =>
        {
            if (result.GetValueOrDefault<long>() <= 0)
            {
                result.AddError("--max-bytes must be a positive Int64 value in bytes.");
            }
        });

        var command = new RootCommand("Generate a UTF-8 file for external sorting.");
        command.Options.Add(dictionary);
        command.Options.Add(output);
        command.Options.Add(maxBytes);
        command.Options.Add(seed);
        command.SetAction(result =>
        {
            var options = new CommandLineOptions(
                result.GetRequiredValue(dictionary),
                result.GetRequiredValue(output),
                result.GetValue(maxBytes),
                result.GetValue(seed));

            return action(ValidatePaths(options));
        });

        return command;
    }

    private static CommandLineOptions ValidatePaths(CommandLineOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.DictionaryPath) || string.IsNullOrWhiteSpace(options.OutputPath))
        {
            throw new ArgumentException("Dictionary and output paths must not be empty.");
        }

        string currentDirectory = AppInfo.CurrentDirectory;
        string dictionaryPath = Path.GetFullPath(options.DictionaryPath, currentDirectory);
        string outputPath = Path.GetFullPath(options.OutputPath, currentDirectory);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (string.Equals(dictionaryPath, outputPath, comparison))
        {
            throw new ArgumentException("Dictionary and output paths must be different.");
        }

        if (File.Exists(outputPath) || Directory.Exists(outputPath))
        {
            throw new IOException($"Output path already exists: '{outputPath}'.");
        }

        return options with { DictionaryPath = dictionaryPath, OutputPath = outputPath };
    }
}
