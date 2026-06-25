namespace LegacyRecordsCaseWareFileUsers.Data.Domain;

/// <summary>
///     A staff member as read from the staff database (the <c>Lookups.ViewActiveStaff</c> view).
/// </summary>
public class Staff
{
    public int StaffNumber { get; set; }

    public string EmailAddress { get; set; } = null!;

    public string FullName { get; set; } = null!;

    public string CaseWareUserIdentifier { get; set; } = null!;

    public string UserPrincipalName { get; set; } = null!;

    public string Office { get; set; } = null!;

    public string Position { get; set; } = null!;

    public string SamAccountName { get; set; } = null!;

    public int? PositionCode { get; set; }
}
