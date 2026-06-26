using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LegacyRecordsCaseWareFileUsers.Data.Contexts;
using Microsoft.EntityFrameworkCore;

namespace LegacyRecordsCaseWareFileUsers.Data.Repositories;

/// <summary>
///     Entity Framework implementation of <see cref="ICaseWareFilesForApplicationsRepository" />.
/// </summary>
internal class CaseWareFilesForApplicationsRepository : ICaseWareFilesForApplicationsRepository
{
    private readonly CaseWareFileManagementDbContext context;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CaseWareFilesForApplicationsRepository" /> class.
    /// </summary>
    /// <param name="context">The CaseWare File Management database context.</param>
    public CaseWareFilesForApplicationsRepository(CaseWareFileManagementDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        this.context = context;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<int, string>> GetUncPathsByIdsAsync(
        IReadOnlyCollection<int> fileIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileIds);

        if (fileIds.Count == 0)
        {
            return new Dictionary<int, string>();
        }

        var rows = await this.context.CaseWareFilesForApplications
                             .Where(o => fileIds.Contains(o.CaseWareFileForApplicationsId))
                             .Select(o => new { o.CaseWareFileForApplicationsId, o.CaseWareFileUncPath })
                             .ToListAsync(cancellationToken)
                             .ConfigureAwait(false);

        return rows.ToDictionary(o => o.CaseWareFileForApplicationsId, o => o.CaseWareFileUncPath);
    }
}
