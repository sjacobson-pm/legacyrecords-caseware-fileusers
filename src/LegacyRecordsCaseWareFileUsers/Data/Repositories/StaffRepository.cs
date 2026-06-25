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
    private readonly StaffDbContext context;

    /// <summary>
    ///     Initializes a new instance of the <see cref="StaffRepository" /> class.
    /// </summary>
    /// <param name="context">The staff database context.</param>
    public StaffRepository(StaffDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        this.context = context;
    }

    /// <inheritdoc />
    public async Task<ICollection<Staff>> GetStaffByCaseWareUserIdentifiersAsync(
        ICollection<string> caseWareUserIdentifiers,
        CancellationToken cancellationToken = default)
    {
        return await this.context.Staff.Where(o => caseWareUserIdentifiers.Contains(o.CaseWareUserIdentifier))
                         .ToListAsync(cancellationToken)
                         .ConfigureAwait(false);
    }
}
