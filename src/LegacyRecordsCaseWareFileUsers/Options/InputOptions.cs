using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace LegacyRecordsCaseWareFileUsers.Options;

/// <summary>
///     The fallback inputs used when the <c>--input-files</c> argument is not supplied.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "There is nothing to test in this class at this point.")]
public class InputOptions
{
    // A text file with one input per line; read when the --input-files command line argument is not
    // supplied. Takes precedence over the inline Files collection below.
    public string FilePath { get; set; } = string.Empty;

    // The fallback collection of inputs used when neither --input-files nor FilePath is supplied.
    // Each entry is either a UNC file path or an integer file identifier.
    public List<string> Files { get; set; } = [];
}
