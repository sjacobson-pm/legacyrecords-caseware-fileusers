using System.Diagnostics.CodeAnalysis;

namespace LegacyRecordsCaseWareFileUsers.Options;

/// <summary>
///     Options controlling how a batch of files is processed.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "There is nothing to test in this class at this point.")]
public class ProcessingOptions
{
    // The maximum number of files processed concurrently. A value of zero or less uses the
    // processor count.
    public int MaxDegreeOfParallelism { get; set; }
}
