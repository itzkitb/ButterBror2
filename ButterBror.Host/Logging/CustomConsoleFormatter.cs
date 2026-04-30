using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using System.Text;

namespace ButterBror.Host.Logging;

public class CustomConsoleFormatter : ConsoleFormatter
{
    public const string FormatterName = "ButterLog";
    private readonly CustomConsoleFormatterOptions _options;

    public CustomConsoleFormatter(IOptionsMonitor<CustomConsoleFormatterOptions> options)
        : base(FormatterName)
    {
        _options = options.CurrentValue;
    }

    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter)
    {
        if (logEntry.Formatter == null)
            return;

        string message = logEntry.Formatter(logEntry.State, logEntry.Exception);
        if (string.IsNullOrEmpty(message) && logEntry.Exception == null)
            return;

        StringBuilder logLine = new StringBuilder();

        // Timestamp
        logLine.Append(DateTime.Now.ToString(@"dd-MM-yy HH:mm:ss.fff"));
        logLine.Append(' ');

        // Log level with color
        AppendColoredLogLevel(logLine, logEntry.LogLevel);

        logLine.Append(message);

        textWriter.Write(logLine.ToString());
        logLine.Clear();

        // Exception details in red
        if (logEntry.Exception != null)
        {
            if (_options.UseColors)
            {
                textWriter.Write("\n\x1b[38;2;255;85;85m"); // Soft red (#FF5555)
                textWriter.WriteLine(logEntry.Exception.ToString());
                textWriter.Write("\x1b[0m");
            }
            else
            {
                textWriter.Write("\n");
                textWriter.WriteLine(logEntry.Exception.ToString());
            }
        }

        // Category and message
        /*logLine.Append(" \x1b[90m- ");
        logLine.Append(logEntry.Category.Replace("ButterBror.", ""));
        logLine.Append("");

        if (logEntry.EventId.Id != 0)
        {
            logLine.Append('[');
            logLine.Append(logEntry.EventId.Id);
            logLine.Append(']');
        }
        logLine.Append("\x1b[0m");*/

        textWriter.WriteLine(logLine.ToString());

        textWriter.Flush();
    }

    private void AppendColoredLogLevel(StringBuilder builder, LogLevel logLevel)
    {
        if (!_options.UseColors)
        {
            builder.Append(GetLogLevelAbbreviation(logLevel));
            builder.Append(' ');
            return;
        }

        string colorCode = _options.UseTrueColor
            ? GetTrueColorCode(logLevel)
            : GetBasicAnsiCode(logLevel);

        string levelText = GetLogLevelAbbreviation(logLevel);

        builder.Append(colorCode);
        builder.Append(levelText);
        builder.Append("\x1b[0m");
        builder.Append(' ');
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
        LogLevel.Trace => "\x1b[90m",       // Dark gray
        LogLevel.Debug => "\x1b[36m",       // Cyan
        LogLevel.Information => "\x1b[32m", // Green
        LogLevel.Warning => "\x1b[33m",     // Yellow
        LogLevel.Error => "\x1b[31m",       // Red
        LogLevel.Critical => "\x1b[35m\x1b[1m", // Magenta + bold
        _ => "\x1b[37m" // White
    };

    private static string GetTrueColorCode(LogLevel logLevel)
    {
        string hexColor = logLevel switch
        {
            LogLevel.Trace => "#6272a4",    // Dracula comment (серо-голубой)
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

        int r = Convert.ToInt32(hex.Substring(0, 2), 16);
        int g = Convert.ToInt32(hex.Substring(2, 2), 16);
        int b = Convert.ToInt32(hex.Substring(4, 2), 16);

        string boldPrefix = isBold ? "\x1b[1m" : "";
        return $"{boldPrefix}\x1b[38;2;{r};{g};{b}m";
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