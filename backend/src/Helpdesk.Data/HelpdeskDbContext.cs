using Helpdesk.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Data;

public class HelpdeskDbContext(DbContextOptions<HelpdeskDbContext> options)
    : DbContext(options)
{
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<StaffProfile> StaffProfiles => Set<StaffProfile>();
    public DbSet<FlightRecord> Flights => Set<FlightRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Ticket>(entity =>
        {
            entity.Property(x => x.Status).HasConversion<string>();
            entity.Property(x => x.Priority).HasConversion<string>();

            entity.Property(x => x.Terminal).HasMaxLength(20);
            entity.Property(x => x.Area).HasMaxLength(100);
            entity.Property(x => x.SystemType).HasMaxLength(100);
            entity.Property(x => x.ReporterName).HasMaxLength(120);
            entity.Property(x => x.ReporterEmail).HasMaxLength(200);
            entity.Property(x => x.StaffId).HasMaxLength(50);
            entity.Property(x => x.Assignee).HasMaxLength(120);
            entity.Property(x => x.Workaround).HasMaxLength(1000);

            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.Priority);
            entity.HasIndex(x => x.CreatedAtUtc);
            entity.HasIndex(x => x.StaffId);
        });

        modelBuilder.Entity<StaffProfile>(entity =>
        {
            entity.HasIndex(x => x.BadgeId).IsUnique();
            entity.Property(x => x.BadgeId).HasMaxLength(50);
            entity.Property(x => x.Name).HasMaxLength(120);
            entity.Property(x => x.Email).HasMaxLength(200);
        });

        modelBuilder.Entity<FlightRecord>(entity =>
        {
            entity.HasIndex(x => x.FlightNumber).IsUnique();
            entity.Property(x => x.FlightNumber).HasMaxLength(20);
            entity.Property(x => x.Destination).HasMaxLength(120);
            entity.Property(x => x.Gate).HasMaxLength(50);
            entity.Property(x => x.Status).HasMaxLength(100);
        });
    }
}
