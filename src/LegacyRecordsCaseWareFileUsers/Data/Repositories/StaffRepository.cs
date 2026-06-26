using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LegacyRecordsCaseWareFileUsers.Data.Contexts;
using LegacyRecordsCaseWareFileUsers.Data.Domain;
using Microsoft.EntityFrameworkCore;

namespace LegacyRecordsCaseWareFileUsers.Data.Repositories;

/// <summary>
///     Entity Framework implementation of <see cref="IStaffRepository" />.
/// </summary>
internal class StaffRepository : IStaffRepository
{
    private readonly CaseWareFileManagementDbContext context;

    /// <summary>
    ///     Initializes a new instance of the <see cref="StaffRepository" /> class.
    /// </summary>
    /// <param name="context">The CaseWare File Management database context.</param>
    public StaffRepository(CaseWareFileManagementDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        this.context = context;
    }

    /// <inheritdoc />
    public async Task<ICollection<Staff>> GetStaffByIdentifiersAsync(
        IReadOnlyCollection<string> caseWareUserIdentifiers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caseWareUserIdentifiers);

        if (caseWareUserIdentifiers.Count == 0)
        {
            return new List<Staff>();
        }

        return await this.context.Staff
                         .Where(o => caseWareUserIdentifiers.Contains(o.CaseWareUserIdentifier))
                         .ToListAsync(cancellationToken)
                         .ConfigureAwait(false);
    }
}
