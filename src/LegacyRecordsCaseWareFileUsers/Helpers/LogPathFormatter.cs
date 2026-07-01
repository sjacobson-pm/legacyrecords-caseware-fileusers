using System.IO;

namespace LegacyRecordsCaseWareFileUsers.Helpers;

/// <summary>
///     Helpers for formatting file paths in log messages. Full UNC paths and per-file workspace
///     paths are verbose and frequently include GUID-named folders that add no value to a log
///     consumer; this class produces a compact form that preserves the file name (and its immediate
///     parent folder, when one exists) so logs remain useful without being a wall of text.
/// </summary>
internal static class LogPathFormatter
{
    /// <summary>
    ///     Returns a compact path representation suitable for log messages: the file name preceded
    ///     by its immediate parent folder when one exists, separated by a backslash. Empty,
    ///     whitespace, and bare-file-name inputs are returned as-is (with whitespace inputs becoming
    ///     an empty string).
    /// </summary>
    /// <param name="path">The path to format. May be null or empty.</param>
    /// <returns>A short, log-friendly representation of <paramref name="path" />.</returns>
    public static string FormatForLog(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var fileName = Path.GetFileName(path);
        var directory = Path.GetDirectoryName(path);

        if (string.IsNullOrEmpty(directory))
        {
            return fileName;
        }

        var parent = Path.GetFileName(directory);

        return string.IsNullOrEmpty(parent) ? fileName : $"{parent}\\{fileName}";
    }
}
