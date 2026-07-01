namespace LegacyRecordsCaseWareFileUsers.Models;

/// <summary>
///     A user assigned to a file that was successfully mapped to staff and retained after support
///     users were removed.
/// </summary>
public record ReportedUser(string FullName, string Office);
