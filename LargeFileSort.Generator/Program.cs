namespace LargeFileSort.Generator;

using System.CommandLine;
using LargeFileSort.Generator.Configuration;

internal class Program
{
    static int Main(string[] args)
    {
        try
        {
            var command = CommandLineParser.CreateCommand(options =>
            {
                var configuration = ConfigurationLoader.Load();
                Console.WriteLine($"Dictionary: {options.DictionaryPath}");
                Console.WriteLine($"Output: {options.OutputPath}");
                Console.WriteLine($"Maximum output bytes: {options.MaxOutputBytes}");
                Console.WriteLine($"Seed: {options.Seed?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "automatic"}");
                Console.WriteLine($"Maximum dictionary bytes: {configuration.MaxDictionaryBytes}");
                Console.WriteLine($"Writer buffer characters: {configuration.WriterBufferCharacters}");
                Console.WriteLine("Parameters validated. File generation is not implemented yet.");
                return 0;
            });

            return command.Parse(args).Invoke(new InvocationConfiguration
            {
                EnableDefaultExceptionHandler = false
            });
        }
        catch (OutOfMemoryException)
        {
            Console.Error.WriteLine("Not enough memory.");
            return 1;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException
                                         or FormatException or NotSupportedException or System.Security.SecurityException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }
}
