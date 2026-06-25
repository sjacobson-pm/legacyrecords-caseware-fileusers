using System.Diagnostics.CodeAnalysis;

namespace LegacyRecordsCaseWareFileUsers.Options;

/// <summary>
///     Options controlling where per-file local workspaces are created.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "There is nothing to test in this class at this point.")]
public class WorkspaceOptions
{
    // The root folder under which a unique per-file workspace is created. When empty, the operating
    // system temp folder is used.
    public string RootPath { get; set; } = string.Empty;
}
