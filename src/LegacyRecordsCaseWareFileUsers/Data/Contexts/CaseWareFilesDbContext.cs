using System.Diagnostics.CodeAnalysis;
using LegacyRecordsCaseWareFileUsers.Data.Domain;
using Microsoft.EntityFrameworkCore;

namespace LegacyRecordsCaseWareFileUsers.Data.Contexts;

/// <summary>
///     Entity Framework context for the file-management database (known CaseWare files).
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Entity Framework context plumbing.")]
internal class CaseWareFilesDbContext : DbContext
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="CaseWareFilesDbContext" /> class.
    /// </summary>
    /// <param name="options">The context options.</param>
    public CaseWareFilesDbContext(DbContextOptions<CaseWareFilesDbContext> options)
        : base(options)
    {
    }

    public DbSet<CaseWareFileForApplications> CaseWareFilesForApplications => this.Set<CaseWareFileForApplications>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<CaseWareFileForApplications>();
        entity.ToTable("CaseWareFilesForApplications", "dbo");
        entity.HasKey(o => o.CaseWareFileForApplicationsId);
    }
}
