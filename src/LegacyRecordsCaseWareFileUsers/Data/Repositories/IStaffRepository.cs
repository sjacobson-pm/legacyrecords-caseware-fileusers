using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LegacyRecordsCaseWareFileUsers.Data.Domain;

namespace LegacyRecordsCaseWareFileUsers.Data.Repositories;

/// <summary>
///     Looks up staff records from the staff database.
/// </summary>
public interface IStaffRepository
{
    /// <summary>
    ///     Returns the staff records whose CaseWare user identifier matches one of the supplied
    ///     identifiers in a single query. The collection is intended to be the union of all
    ///     identifiers seen across a batch so the database is hit at most once per run.
    /// </summary>
    /// <param name="caseWareUserIdentifiers">The CaseWare user identifiers to look up.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>The matching staff records.</returns>
    Task<ICollection<Staff>> GetStaffByIdentifiersAsync(
        IReadOnlyCollection<string> caseWareUserIdentifiers,
        CancellationToken cancellationToken = default);
}
