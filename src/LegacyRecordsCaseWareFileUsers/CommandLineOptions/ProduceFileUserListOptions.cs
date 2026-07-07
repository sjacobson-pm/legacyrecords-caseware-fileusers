using System.Diagnostics.CodeAnalysis;
using CommandLine;

namespace LegacyRecordsCaseWareFileUsers.CommandLineOptions;

/// <summary>
///     Command line options for producing the file-users spreadsheet.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "There is nothing to test in this class at this point.")]
public class ProduceFileUserListOptions
{
    [Option('i', "input-files", Required = false, HelpText = "Text file with one input per line: a UNC path to a .ac_ file or an integer file ID.")]
    public string? InputFilesPath { get; set; }

    [Option('o', "output", Required = false, HelpText = "Full path of the .xlsx spreadsheet to write.")]
    public string? OutputPath { get; set; }

    [Option('r', "resume", Required = false, HelpText = "Resume a previously-crashed run by RunID (e.g., swift-otter-runs). Fails if the journal file for that RunID is not found in the output folder, or if it has already been marked completed.")]
    public string? ResumeRunId { get; set; }
}
