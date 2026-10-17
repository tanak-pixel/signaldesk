using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

public class SignalDeskDbContext(
    DbContextOptions<SignalDeskDbContext> options
) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<SupportReport> Reports => Set<SupportReport>();
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<IncidentActivity> IncidentActivities =>
        Set<IncidentActivity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var report = modelBuilder.Entity<SupportReport>();

        report.HasKey(r => r.Id);

        report.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(r => r.ReporterId)
            .OnDelete(DeleteBehavior.SetNull);

        report.HasOne<Incident>()
            .WithMany()
            .HasForeignKey(r => r.IncidentId)
            .OnDelete(DeleteBehavior.Restrict);

        report.Property(r => r.Title).HasMaxLength(120);
        report.Property(r => r.Description).HasMaxLength(2000);
        report.Property(r => r.Service).HasMaxLength(30);
        report.Property(r => r.Location).HasMaxLength(120);
        report.Property(r => r.Status).HasMaxLength(30);
        report.Property(r => r.Version).IsConcurrencyToken();

        report.HasIndex(r => r.CreatedAt);

        var incident = modelBuilder.Entity<Incident>();

        incident.HasKey(i => i.Id);

        incident.Property(i => i.Title).HasMaxLength(120);
        incident.Property(i => i.Description).HasMaxLength(2000);
        incident.Property(i => i.Service).HasMaxLength(30);
        incident.Property(i => i.Status).HasMaxLength(30);
        incident.Property(i => i.Version).IsConcurrencyToken();

        incident.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(i => i.AssignedTechnicianId)
            .OnDelete(DeleteBehavior.SetNull);

        incident.HasIndex(i => new { i.Service, i.Status });
        incident.HasIndex(i => i.CreatedAt);

        var activity = modelBuilder.Entity<IncidentActivity>();

        activity.HasKey(a => a.Id);

        activity.HasOne<Incident>()
            .WithMany()
            .HasForeignKey(a => a.IncidentId)
            .OnDelete(DeleteBehavior.Restrict);

        activity.Property(a => a.ActorName).HasMaxLength(256);
        activity.Property(a => a.Action).HasMaxLength(40);
        activity.Property(a => a.Message).HasMaxLength(500);

        activity.HasIndex(a => new { a.IncidentId, a.CreatedAt });
    }
}