using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace LegacyRecordsCaseWareFileUsers.Options;

/// <summary>
///     The fallback inputs used when the <c>--input-files</c> argument is not supplied.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "There is nothing to test in this class at this point.")]
public class InputOptions
{
    // The fallback collection of inputs used when the --input-files command line argument is not
    // supplied. Each entry is either a UNC file path or an integer file identifier.
    public List<string> Files { get; set; } = new();
}
