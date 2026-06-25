using System.Diagnostics.CodeAnalysis;
using LegacyRecordsCaseWareFileUsers.Data.Domain;
using Microsoft.EntityFrameworkCore;

namespace LegacyRecordsCaseWareFileUsers.Data.Contexts;

/// <summary>
///     Entity Framework context for the staff database.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Entity Framework context plumbing.")]
internal class StaffDbContext : DbContext
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="StaffDbContext" /> class.
    /// </summary>
    /// <param name="options">The context options.</param>
    public StaffDbContext(DbContextOptions<StaffDbContext> options)
        : base(options)
    {
    }

    public DbSet<Staff> Staff => this.Set<Staff>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Staff>();
        entity.ToTable("ViewActiveStaff", "Lookups");
        entity.HasKey(o => o.StaffNumber);
    }
}
