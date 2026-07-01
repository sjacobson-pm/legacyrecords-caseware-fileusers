using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LegacyRecordsCaseWareFileUsers.Data.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LegacyRecordsCaseWareFileUsers.Data.Repositories;

/// <summary>
///     Entity Framework implementation of <see cref="ICaseWareFilesForApplicationsRepository" />. The
///     lookup is executed in bounded chunks so that a batch of thousands of file IDs never produces a
///     single oversized <c>WHERE Id IN (...)</c> query — smaller queries keep the SQL Server plan cache
///     healthier and stay well clear of the 2,100 parameter cap on older EF Core translation modes.
/// </summary>
internal class CaseWareFilesForApplicationsRepository : ICaseWareFilesForApplicationsRepository
{
    // A conservative chunk size that keeps each query well under SQL Server's 2,100 parameter cap
    // and produces plan-cacheable query text for typical batches.
    private const int QueryChunkSize = 1000;

    private readonly CaseWareFileManagementDbContext context;
    private readonly ILogger<CaseWareFilesForApplicationsRepository> logger;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CaseWareFilesForApplicationsRepository" /> class.
    /// </summary>
    /// <param name="context">The CaseWare File Management database context.</param>
    /// <param name="logger">The logger.</param>
    public CaseWareFilesForApplicationsRepository(
        CaseWareFileManagementDbContext context,
        ILogger<CaseWareFilesForApplicationsRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(logger);

        this.context = context;
        this.logger = logger;
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

        var result = new Dictionary<int, string>(fileIds.Count);
        var chunks = fileIds.Chunk(QueryChunkSize).ToList();

        if (chunks.Count > 1)
        {
            this.logger.LogInformation(
                "Resolving {FileIdCount} file ID(s) in {ChunkCount} chunk(s) of up to {ChunkSize}.",
                fileIds.Count,
                chunks.Count,
                QueryChunkSize);
        }

        var chunkIndex = 0;

        foreach (var chunk in chunks)
        {
            chunkIndex++;

            if (chunks.Count > 1)
            {
                this.logger.LogDebug(
                    "Resolving file-ID chunk {ChunkIndex}/{ChunkCount} ({ChunkSize} ID(s))...",
                    chunkIndex,
                    chunks.Count,
                    chunk.Length);
            }

            var rows = await this.context.CaseWareFilesForApplications
                                 .Where(o => chunk.Contains(o.CaseWareFileForApplicationsId))
                                 .Select(o => new { o.CaseWareFileForApplicationsId, o.CaseWareFileUncPath })
                                 .ToListAsync(cancellationToken)
                                 .ConfigureAwait(false);

            foreach (var row in rows)
            {
                result[row.CaseWareFileForApplicationsId] = row.CaseWareFileUncPath;
            }
        }

        return result;
    }
}
