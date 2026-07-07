using System.Diagnostics.CodeAnalysis;

namespace LegacyRecordsCaseWareFileUsers.Options;

/// <summary>
///     Options controlling where the generated spreadsheet is written.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "There is nothing to test in this class at this point.")]
public class OutputOptions
{
    // The directory the generated spreadsheet is written to when an explicit file path is not
    // supplied. When empty, the current working directory is used.
    public string Directory { get; set; } = string.Empty;

    // A fully-qualified spreadsheet path used when the --output command line argument is not
    // supplied. When empty, a timestamped file name is generated in Directory.
    public string FilePath { get; set; } = string.Empty;

    // The depth of the "share" prefix in the UNC path (1-based). The Share column in the
    // spreadsheet contains the first ShareSegmentIndex segments of the UNC path (for example,
    // \\<server>\<smb-share>\<share>); the File column contains everything below the share
    // prefix, so Share + "\" + File reconstructs the original UNC path for any depth. The
    // default of 3 targets the first subfolder inside the SMB share for paths of the form
    // \\<server>\<smb-share>\<share>\<engagement>\<file.ac_>. Change this if the archive layout
    // shifts and a different segment better represents "which archive volume did this come from"
    // for filtering purposes. Values less than 1 are treated as 1; paths shallower than the
    // configured index produce an empty Share and the full path is shown in the File column.
    public int ShareSegmentIndex { get; set; } = 3;
}
