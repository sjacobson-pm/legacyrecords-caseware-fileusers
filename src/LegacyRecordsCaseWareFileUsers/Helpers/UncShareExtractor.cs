using System;
using System.Linq;

namespace LegacyRecordsCaseWareFileUsers.Helpers;

/// <summary>
///     Extracts the "share prefix" of a UNC-style path for grouping in the spreadsheet output.
///     Given a path of the form <c>\\network\volume\share\engagement\file.ac_</c>, returns the
///     first <em>N</em> path segments joined with backslashes and prefixed with <c>\\</c>, where
///     <em>N</em> is the configured <c>Output:ShareSegmentIndex</c> (default 3 — the first
///     subfolder under the SMB share). Segment 1 is the server, segment 2 is the SMB share,
///     segment 3 is the first subfolder, and so on.
///     <para>
///         Returning the prefix (rather than just the segment at index N) means the full UNC path
///         can be reconstructed from a spreadsheet row: <c>Share + "\" + File</c>. The File column
///         then holds everything after the share prefix, no matter how deep the original path
///         went.
///     </para>
///     <para>
///         Falls back to an empty string when the input is null/whitespace or when the path has
///         fewer segments than the configured index — those rows still appear in the output; they
///         just have no Share value and Excel's autofilter can find them by filtering on blank.
///     </para>
/// </summary>
internal static class UncShareExtractor
{
    private static readonly char[] Separators = { '\\', '/' };

    /// <summary>
    ///     Extracts the share prefix — the first <paramref name="oneBasedSegmentIndex" /> segments
    ///     of the UNC path joined with backslashes and prefixed with <c>\\</c>.
    /// </summary>
    /// <param name="uncPath">The UNC path to inspect. May be <c>null</c> or empty.</param>
    /// <param name="oneBasedSegmentIndex">
    ///     The 1-based index of the deepest segment included in the prefix. Values less than 1
    ///     are treated as 1.
    /// </param>
    /// <returns>
    ///     The share prefix (for example, <c>\\network\volume\share</c> when the input is
    ///     <c>\\network\volume\share\engagement\file.ac_</c> and the index is 3), or an empty
    ///     string when the path is null/empty or too shallow.
    /// </returns>
    public static string Extract(string? uncPath, int oneBasedSegmentIndex)
    {
        if (string.IsNullOrWhiteSpace(uncPath))
        {
            return string.Empty;
        }

        var index = oneBasedSegmentIndex < 1 ? 1 : oneBasedSegmentIndex;

        // A UNC path starts with "\\" — the split with RemoveEmptyEntries drops those leading
        // empty segments and yields just the meaningful path components.
        var segments = uncPath.Split(Separators, StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length < index)
        {
            return string.Empty;
        }

        var joined = string.Join('\\', segments.Take(index));

        // Preserve the UNC leading double-backslash convention when the input was a UNC path.
        // Forward-slash-form UNC input (\/\/server/…) is normalized to backslash form in output
        // so downstream string operations don't have to consider both separator styles.
        var isUnc = uncPath.StartsWith("\\\\", StringComparison.Ordinal)
                 || uncPath.StartsWith("//", StringComparison.Ordinal);

        return isUnc ? $@"\\{joined}" : joined;
    }
}
