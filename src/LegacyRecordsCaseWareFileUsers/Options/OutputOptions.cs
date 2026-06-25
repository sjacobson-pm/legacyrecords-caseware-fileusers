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
}
