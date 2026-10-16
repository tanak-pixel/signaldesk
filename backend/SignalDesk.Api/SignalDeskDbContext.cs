using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

public class SignalDeskDbContext(
    DbContextOptions<SignalDeskDbContext> options
) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<SupportReport> Reports => Set<SupportReport>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configure Identity's user, role, and account tables.
        base.OnModelCreating(modelBuilder);

        var report = modelBuilder.Entity<SupportReport>();

        report.HasKey(r => r.Id);
        report.Property(r => r.Title).HasMaxLength(120);
        report.Property(r => r.Description).HasMaxLength(2000);
        report.Property(r => r.Service).HasMaxLength(30);
        report.Property(r => r.Location).HasMaxLength(120);
        report.Property(r => r.Status).HasMaxLength(30);
        report.Property(r => r.Version).IsConcurrencyToken();
        report.HasIndex(r => r.CreatedAt);
    }
}