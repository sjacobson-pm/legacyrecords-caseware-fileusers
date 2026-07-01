using System.Diagnostics.CodeAnalysis;

namespace LegacyRecordsCaseWareFileUsers.Options;

[ExcludeFromCodeCoverage(Justification = "There is nothing to test in this class at this point.")]
public class LoggingOptions
{
    public string ConsoleOutputTemplate { get; set; } = null!;

    public string DebugOutputTemplate { get; set; } = null!;

    public LogLevelOptions LogLevel { get; set; } = null!;

    public LoggingApplicationInsightsOptions ApplicationInsights { get; set; } = null!;

    public LoggingFileOptions File { get; set; } = new();
}
