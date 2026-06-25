using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LegacyRecordsCaseWareFileUsers.Data.Contexts;
using Microsoft.EntityFrameworkCore;

namespace LegacyRecordsCaseWareFileUsers.Data.Repositories;

/// <summary>
///     Entity Framework implementation of <see cref="IFilePathRepository" />.
/// </summary>
internal class FilePathRepository : IFilePathRepository
{
    private readonly CaseWareFilesDbContext context;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FilePathRepository" /> class.
    /// </summary>
    /// <param name="context">The CaseWare files database context.</param>
    public FilePathRepository(CaseWareFilesDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        this.context = context;
    }

    /// <inheritdoc />
    public async Task<string?> TryGetUncPathAsync(int fileId, CancellationToken cancellationToken = default)
    {
        return await this.context.CaseWareFilesForApplications.Where(o => o.CaseWareFileForApplicationsId == fileId)
                         .Select(o => o.CaseWareFileUncPath)
                         .SingleOrDefaultAsync(cancellationToken)
                         .ConfigureAwait(false);
    }
}
