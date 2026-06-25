using System.Diagnostics.CodeAnalysis;

namespace LegacyRecordsCaseWareFileUsers.Options;

/// <summary>
///     Options for the Active Directory lookup used to identify CaseWare support users.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "There is nothing to test in this class at this point.")]
public class ActiveDirectoryOptions
{
    public string DomainName { get; set; } = string.Empty;

    public string CaseWareSupportTeamGroupName { get; set; } = string.Empty;
}
