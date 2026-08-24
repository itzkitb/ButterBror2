using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using System.Text;

namespace ButterBror.Host.Logging;

public class CustomConsoleFormatter(IOptionsMonitor<CustomConsoleFormatterOptions> options)
    : ConsoleFormatter(FormatterName)
{
    public const string FormatterName = "bb_logger";
    private readonly CustomConsoleFormatterOptions _options = options.CurrentValue;

    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter)
    {
        var message = logEntry.Formatter(logEntry.State, logEntry.Exception);
        if (string.IsNullOrEmpty(message) && logEntry.Exception == null)
            return;

        var logLine = new StringBuilder();
        logLine.Append(DateTime.Now.ToString(@"HH:mm:ss"));
        logLine.Append(' ');
        
        AppendColoredLogLevel(logLine, logEntry.LogLevel);
        logLine.Append(' ');
        
        logLine.Append(message);
        
        if (logEntry.Exception != null)
        {
            if (_options.UseColors)
            {
                logLine.Append("\n\e[38;2;255;85;85m");
                logLine.AppendLine(logEntry.Exception.ToString());
                logLine.Append("\e[0m");
            }
            else
            {
                logLine.Append('\n');
                logLine.AppendLine(logEntry.Exception.ToString());
            }
        }

        textWriter.WriteLine(logLine.ToString());
        textWriter.Flush();
    }

    private void AppendColoredLogLevel(StringBuilder builder, LogLevel logLevel)
    {
        if (!_options.UseColors)
        {
            builder.Append(GetLogLevelAbbreviation(logLevel));
            return;
        }

        var colorCode = _options.UseTrueColor
            ? GetTrueColorCode(logLevel)
            : GetBasicAnsiCode(logLevel);

        var levelText = GetLogLevelAbbreviation(logLevel);

        builder.Append(colorCode);
        builder.Append(levelText);
        builder.Append("\e[0m");
    }

    private static string GetLogLevelAbbreviation(LogLevel logLevel) => logLevel switch
    {
        LogLevel.Trace => "·",
        LogLevel.Debug => "◇",
        LogLevel.Information => "•",
        LogLevel.Warning => "△",
        LogLevel.Error => "▲",
        LogLevel.Critical => "◈",
        _ => "n"
    };

    private static string GetBasicAnsiCode(LogLevel logLevel) => logLevel switch
    {
        LogLevel.Trace => "\e[90m",       // Dark gray
        LogLevel.Debug => "\e[36m",       // Cyan
        LogLevel.Information => "\e[32m", // Green
        LogLevel.Warning => "\e[33m",     // Yellow
        LogLevel.Error => "\e[31m",       // Red
        LogLevel.Critical => "\e[35m\e[1m", // Magenta + bold
        _ => "\e[37m" // White
    };

    private static string GetTrueColorCode(LogLevel logLevel)
    {
        var hexColor = logLevel switch
        {
            LogLevel.Trace => "#6272a4",    // Dracula comment
            LogLevel.Debug => "#8be9fd",    // Dracula cyan
            LogLevel.Information => "#50fa7b", // Dracula green
            LogLevel.Warning => "#f1fa8c",  // Dracula yellow
            LogLevel.Error => "#ff5555",    // Soft red
            LogLevel.Critical => "#ff79c6", // Dracula pink + bold
            _ => "#ffffff" // White
        };

        return HexToAnsiEscape(hexColor, isBold: logLevel == LogLevel.Critical);
    }

    private static string HexToAnsiEscape(string hex, bool isBold = false)
    {
        hex = hex.TrimStart('#');

        var r = Convert.ToInt32(hex.Substring(0, 2), 16);
        var g = Convert.ToInt32(hex.Substring(2, 2), 16);
        var b = Convert.ToInt32(hex.Substring(4, 2), 16);

        var boldPrefix = isBold ? "\e[1m" : "";
        return $"{boldPrefix}\e[38;2;{r};{g};{b}m";
    }
}

public class CustomConsoleFormatterOptions : ConsoleFormatterOptions
{
    public bool UseColors { get; set; } = true;
    public bool UseTrueColor { get; set; } = IsTrueColorSupported();

    private static bool IsTrueColorSupported()
    {
        var term = Environment.GetEnvironmentVariable("TERM");
        var colorterm = Environment.GetEnvironmentVariable("COLORTERM");

        return OperatingSystem.IsWindows()
            ? Environment.GetEnvironmentVariable("WT_SESSION") != null // Windows Terminal
            : (colorterm?.Contains("truecolor", StringComparison.OrdinalIgnoreCase) == true ||
               colorterm?.Contains("24bit", StringComparison.OrdinalIgnoreCase) == true ||
               term?.Contains("truecolor", StringComparison.OrdinalIgnoreCase) == true);
    }
}