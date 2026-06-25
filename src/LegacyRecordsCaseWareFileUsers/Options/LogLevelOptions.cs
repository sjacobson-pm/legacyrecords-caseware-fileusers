using System.Diagnostics.CodeAnalysis;

namespace LegacyRecordsCaseWareFileUsers.Options;

[ExcludeFromCodeCoverage(Justification = "There is nothing to test in this class at this point.")]
public class LogLevelOptions
{
    public string Console { get; set; } = null!;

    public string Debug { get; set; } = null!;

    public string Default { get; set; } = null!;

    public string ApplicationInsights { get; set; } = null!;

    public string Microsoft { get; set; } = null!;

    public string System { get; set; } = null!;
}
