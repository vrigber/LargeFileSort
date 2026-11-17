using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace LargeFileSort.Sorter.Configuration;

internal static class LoggingConfiguration
{
    public static ILoggerFactory CreateFactory(IConfiguration configuration) =>
        LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Information)
            .AddConfiguration(configuration.GetSection("Logging"))
            .AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace)
            .AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
                options.ColorBehavior = LoggerColorBehavior.Disabled;
            }));
}
