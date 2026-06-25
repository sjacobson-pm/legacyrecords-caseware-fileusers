using System.Collections.Generic;
using LegacyRecordsCaseWareFileUsers.Data.Domain;

namespace LegacyRecordsCaseWareFileUsers.Services.Interfaces;

/// <summary>
///     Removes CaseWare support staff from a collection of file users.
/// </summary>
public interface ISupportUserFilter
{
    /// <summary>
    ///     Removes any staff that are members of the configured CaseWare support team Active Directory
    ///     group. If the group cannot be queried, the supplied collection is returned unchanged.
    /// </summary>
    /// <param name="staff">The staff assigned to a file.</param>
    /// <returns>The staff with support users removed.</returns>
    ICollection<Staff> RemoveSupportUsers(ICollection<Staff> staff);
}
