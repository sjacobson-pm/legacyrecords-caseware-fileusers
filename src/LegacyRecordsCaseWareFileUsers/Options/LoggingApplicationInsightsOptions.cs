using System.Diagnostics.CodeAnalysis;

namespace LegacyRecordsCaseWareFileUsers.Options;

[ExcludeFromCodeCoverage(Justification = "There is nothing to test in this class at this point.")]
public class LoggingApplicationInsightsOptions
{
    public string? ConnectionString { get; set; } = null!;
}
