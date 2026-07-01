namespace LegacyRecordsCaseWareFileUsers.Models;

/// <summary>
///     A user assigned to a file that was successfully mapped to staff and retained after support
///     users were removed.
/// </summary>
/// <param name="FullName">The staff member's full name.</param>
/// <param name="Office">The staff member's office.</param>
/// <param name="Position">The staff member's position title (for example, "Consultant" or
/// "Partner").</param>
public record ReportedUser(string FullName, string Office, string Position);
