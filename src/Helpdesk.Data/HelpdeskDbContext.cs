using Helpdesk.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Data;

public class HelpdeskDbContext : DbContext
{
    public HelpdeskDbContext(DbContextOptions<HelpdeskDbContext> options) : base(options) { }

    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<Technician> Technicians => Set<Technician>();
    public DbSet<TicketNote> TicketNotes => Set<TicketNote>();
    public DbSet<TicketAuditEntry> TicketAuditEntries => Set<TicketAuditEntry>();
    public DbSet<TicketLink> TicketLinks => Set<TicketLink>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Ticket>(entity =>
        {
            entity.ToTable("Tickets");
            entity.HasKey(t => t.Id);

            entity.Property(t => t.Title).HasMaxLength(200).IsRequired();
            entity.Property(t => t.Description).IsRequired();
            entity.Property(t => t.SubmitterName).HasMaxLength(120).IsRequired();
            entity.Property(t => t.SubmitterEmail).HasMaxLength(160).IsRequired();
            entity.Property(t => t.ClosedReason).HasMaxLength(500);

            entity.Property(t => t.Status).HasConversion<int>();
            entity.Property(t => t.Priority).HasConversion<int>();
            entity.Property(t => t.Type).HasConversion<int>();

            entity.HasOne(t => t.AssignedTechnician)
                .WithMany(tech => tech.AssignedTickets)
                .HasForeignKey(t => t.AssignedTechnicianId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(t => new { t.AssignedTechnicianId, t.Status });
            entity.HasIndex(t => t.CreatedAt);
            entity.HasIndex(t => t.Priority);
        });

        modelBuilder.Entity<Technician>(entity =>
        {
            entity.ToTable("Technicians");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Name).HasMaxLength(120).IsRequired();
            entity.Property(t => t.Email).HasMaxLength(160).IsRequired();
        });

        modelBuilder.Entity<TicketNote>(entity =>
        {
            entity.ToTable("TicketNotes");
            entity.HasKey(n => n.Id);

            entity.Property(n => n.AuthorId).HasMaxLength(120);
            entity.Property(n => n.AuthorName).HasMaxLength(120);
            entity.Property(n => n.Content).IsRequired();
            entity.Property(n => n.IsInternal).HasDefaultValue(false);
            entity.Property(n => n.CreatedAt);

            entity.HasOne(n => n.Ticket)
                .WithMany(t => t.Notes)
                .HasForeignKey(n => n.TicketId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TicketAuditEntry>(entity =>
        {
            entity.ToTable("TicketAuditEntries");
            entity.HasKey(a => a.Id);

            entity.Property(a => a.ChangedBy).HasMaxLength(120).IsRequired();
            entity.Property(a => a.Action).HasMaxLength(50).IsRequired();
            entity.Property(a => a.Comment).HasMaxLength(1000);
            entity.Property(a => a.Timestamp);

            entity.HasOne(a => a.Ticket)
                .WithMany(t => t.AuditEntries)
                .HasForeignKey(a => a.TicketId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TicketLink>(entity =>
        {
            entity.ToTable("TicketLinks");
            entity.HasKey(l => l.Id);

            entity.Property(l => l.CreatedById).HasMaxLength(120);
            entity.Property(l => l.LinkType).HasConversion<int>();

            entity.HasOne(l => l.SourceTicket)
                .WithMany(t => t.Links)
                .HasForeignKey(l => l.SourceTicketId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(l => l.TargetTicket)
                .WithMany()
                .HasForeignKey(l => l.TargetTicketId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(l => new { l.SourceTicketId, l.TargetTicketId });
        });
    }
}
