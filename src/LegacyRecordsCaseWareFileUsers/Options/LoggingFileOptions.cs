using System.Diagnostics.CodeAnalysis;

namespace LegacyRecordsCaseWareFileUsers.Options;

/// <summary>
///     Options for the Serilog rolling-file sink. When <see cref="Path" /> is blank, the file sink
///     is not added.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "There is nothing to test in this class at this point.")]
public class LoggingFileOptions
{
    // The path of the rolling log file (the rolling-interval suffix is appended by Serilog).
    // Relative paths are resolved against the current working directory. When blank, the file sink
    // is not added.
    public string Path { get; set; } = string.Empty;

    // The rolling interval used by the file sink: one of Infinite, Year, Month, Day, Hour, Minute.
    // Defaults to Day when blank or invalid.
    public string RollingInterval { get; set; } = "Day";

    // The maximum number of rolled files to keep on disk; older files are deleted. Defaults to 31.
    public int RetainedFileCountLimit { get; set; } = 31;

    // The output template used by the file sink. When blank, the same template as the console sink
    // is used.
    public string OutputTemplate { get; set; } = string.Empty;
}
