using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace LegacyRecordsCaseWareFileUsers.Options;

/// <summary>
///     Database connection strings used by the application.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "There is nothing to test in this class at this point.")]
public class ConnectionStringOptions
{
    public const string SectionName = "ConnectionStrings";

    [Required]
    public string CaseWareFileManagement { get; set; } = string.Empty;

    [Required]
    public string CaseWareUsers { get; set; } = string.Empty;
}
