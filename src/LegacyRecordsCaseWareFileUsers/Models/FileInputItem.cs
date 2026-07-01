namespace LegacyRecordsCaseWareFileUsers.Models;

/// <summary>
///     A single parsed input value: either an integer file identifier or a UNC file path.
/// </summary>
public class FileInputItem(string rawValue, int? fileId, string? uncPath)
{
    public string RawValue { get; } = rawValue;

    public int? FileId { get; } = fileId;

    public string? UncPath { get; } = uncPath;

    public bool IsFileId => this.FileId.HasValue;
}
