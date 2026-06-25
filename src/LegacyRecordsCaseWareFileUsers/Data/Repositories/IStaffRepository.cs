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
    ///     identifiers.
    /// </summary>
    /// <param name="caseWareUserIdentifiers">The CaseWare user identifiers to look up.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>The matching staff records.</returns>
    Task<ICollection<Staff>> GetStaffByCaseWareUserIdentifiersAsync(
        ICollection<string> caseWareUserIdentifiers,
        CancellationToken cancellationToken = default);
}
