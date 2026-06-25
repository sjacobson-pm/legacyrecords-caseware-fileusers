using System.Threading;
using System.Threading.Tasks;

namespace LegacyRecordsCaseWareFileUsers.Services.Interfaces;

/// <summary>
///     Orchestrates producing the file-users spreadsheet from the supplied inputs.
/// </summary>
public interface IFileUserService
{
    /// <summary>
    ///     Processes the supplied inputs and produces the file-users spreadsheet.
    /// </summary>
    /// <param name="inputFilesPath">Optional path to a text file of inputs (UNC paths or file IDs).</param>
    /// <param name="outputPath">Optional full path of the spreadsheet to write.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task RunAsync(string? inputFilesPath, string? outputPath, CancellationToken cancellationToken = default);
}
