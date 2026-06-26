using System.Diagnostics.CodeAnalysis;
using LegacyRecordsCaseWareFileUsers.Data.Domain;
using Microsoft.EntityFrameworkCore;

namespace LegacyRecordsCaseWareFileUsers.Data.Contexts;

/// <summary>
///     Entity Framework context for the CaseWare File Management database (known files and staff).
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Entity Framework context plumbing.")]
internal class CaseWareFileManagementDbContext : DbContext
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="CaseWareFileManagementDbContext" /> class.
    /// </summary>
    /// <param name="options">The context options.</param>
    public CaseWareFileManagementDbContext(DbContextOptions<CaseWareFileManagementDbContext> options)
        : base(options)
    {
    }

    public DbSet<CaseWareFileForApplications> CaseWareFilesForApplications => this.Set<CaseWareFileForApplications>();

    public DbSet<Staff> Staff => this.Set<Staff>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var files = modelBuilder.Entity<CaseWareFileForApplications>();
        files.ToTable("CaseWareFilesForApplications", "dbo");
        files.HasKey(o => o.CaseWareFileForApplicationsId);

        var staff = modelBuilder.Entity<Staff>();
        staff.ToTable("ViewActiveStaff", "Lookups");
        staff.HasKey(o => o.StaffNumber);
    }
}
