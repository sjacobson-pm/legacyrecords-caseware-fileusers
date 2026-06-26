using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LegacyRecordsCaseWareFileUsers.Data.Repositories;

/// <summary>
///     Resolves the UNC paths of known CaseWare files from the file-management database.
/// </summary>
public interface ICaseWareFilesForApplicationsRepository
{
    /// <summary>
    ///     Resolves the UNC paths of known files by their identifiers in a single query.
    /// </summary>
    /// <param name="fileIds">The CaseWare-file-for-applications identifiers to resolve.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>
    ///     A dictionary keyed by file identifier whose values are the matching UNC paths. Identifiers
    ///     with no matching file are absent from the dictionary.
    /// </returns>
    Task<IReadOnlyDictionary<int, string>> GetUncPathsByIdsAsync(
        IReadOnlyCollection<int> fileIds,
        CancellationToken cancellationToken = default);
}
