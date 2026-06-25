namespace LegacyRecordsCaseWareFileUsers.Data.Domain;

/// <summary>
///     A known CaseWare file as read from the file-management database
///     (the <c>dbo.CaseWareFilesForApplications</c> table).
/// </summary>
public class CaseWareFileForApplications
{
    public int CaseWareFileForApplicationsId { get; set; }

    public string CaseWareFileUncPath { get; set; } = null!;
}
