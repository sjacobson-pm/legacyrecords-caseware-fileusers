namespace LegacyRecordsCaseWareFileUsers.Models;

/// <summary>
///     A user assigned to a file that was successfully mapped to staff and retained after support
///     users were removed.
/// </summary>
public class ReportedUser
{
    public ReportedUser(string fullName, string office)
    {
        this.FullName = fullName;
        this.Office = office;
    }

    public string FullName { get; }

    public string Office { get; }
}
