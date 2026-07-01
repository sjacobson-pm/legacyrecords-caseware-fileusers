using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LegacyRecordsCaseWareFileUsers.Data.Contexts;
using LegacyRecordsCaseWareFileUsers.Data.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LegacyRecordsCaseWareFileUsers.Data.Repositories;

/// <summary>
///     Entity Framework implementation of <see cref="IStaffRepository" />. The lookup is executed in
///     bounded chunks so that a batch surfacing thousands of unique identifiers never produces a
///     single oversized <c>WHERE Identifier IN (...)</c> query — smaller queries keep the SQL Server
///     plan cache healthier and stay well clear of the 2,100 parameter cap on older EF Core
///     translation modes.
/// </summary>
internal class StaffRepository : IStaffRepository
{
    // A conservative chunk size that keeps each query well under SQL Server's 2,100 parameter cap
    // and produces plan-cacheable query text for typical batches.
    private const int QueryChunkSize = 1000;

    private readonly CaseWareFileManagementDbContext context;
    private readonly ILogger<StaffRepository> logger;

    /// <summary>
    ///     Initializes a new instance of the <see cref="StaffRepository" /> class.
    /// </summary>
    /// <param name="context">The CaseWare File Management database context.</param>
    /// <param name="logger">The logger.</param>
    public StaffRepository(CaseWareFileManagementDbContext context, ILogger<StaffRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(logger);

        this.context = context;
        this.logger = logger;
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

        var result = new List<Staff>(caseWareUserIdentifiers.Count);
        var chunks = caseWareUserIdentifiers.Chunk(QueryChunkSize).ToList();

        if (chunks.Count > 1)
        {
            this.logger.LogInformation(
                "Resolving staff for {IdentifierCount} identifier(s) in {ChunkCount} chunk(s) of up to {ChunkSize}.",
                caseWareUserIdentifiers.Count,
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
                    "Resolving staff chunk {ChunkIndex}/{ChunkCount} ({ChunkSize} identifier(s))...",
                    chunkIndex,
                    chunks.Count,
                    chunk.Length);
            }

            var rows = await this.context.Staff
                                 .Where(o => chunk.Contains(o.CaseWareUserIdentifier))
                                 .ToListAsync(cancellationToken)
                                 .ConfigureAwait(false);

            result.AddRange(rows);
        }

        return result;
    }
}
