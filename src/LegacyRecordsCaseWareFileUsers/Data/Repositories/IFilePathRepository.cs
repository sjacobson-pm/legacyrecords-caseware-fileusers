using System.Threading;
using System.Threading.Tasks;

namespace LegacyRecordsCaseWareFileUsers.Data.Repositories;

/// <summary>
///     Resolves the UNC paths of known CaseWare files from the file-management database.
/// </summary>
public interface IFilePathRepository
{
    /// <summary>
    ///     Resolves the UNC path of a known file by its identifier.
    /// </summary>
    /// <param name="fileId">The CaseWare-file-for-applications identifier.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>The UNC path, or <c>null</c> if no matching file exists.</returns>
    Task<string?> TryGetUncPathAsync(int fileId, CancellationToken cancellationToken = default);
}
