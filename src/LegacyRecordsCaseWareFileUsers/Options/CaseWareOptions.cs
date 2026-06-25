using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace LegacyRecordsCaseWareFileUsers.Options;

/// <summary>
///     Options controlling how CaseWare files are opened and read.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "There is nothing to test in this class at this point.")]
public class CaseWareOptions
{
    public const string SectionName = "CaseWare";

    [Required]
    public string LoginUserId { get; set; } = string.Empty;

    [Required]
    public string LoginUserPassword { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int RetryRetrievingUsersMaximumAttempts { get; set; }
}
