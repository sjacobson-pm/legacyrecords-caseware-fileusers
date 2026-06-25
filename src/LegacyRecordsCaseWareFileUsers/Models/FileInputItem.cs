namespace LegacyRecordsCaseWareFileUsers.Models;

/// <summary>
///     A single parsed input value: either an integer file identifier or a UNC file path.
/// </summary>
public class FileInputItem
{
    public FileInputItem(string rawValue, int? fileId, string? uncPath)
    {
        this.RawValue = rawValue;
        this.FileId = fileId;
        this.UncPath = uncPath;
    }

    public string RawValue { get; }

    public int? FileId { get; }

    public string? UncPath { get; }

    public bool IsFileId => this.FileId.HasValue;
}
