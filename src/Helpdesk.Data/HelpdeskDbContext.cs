namespace Helpdesk.Data;

using System.Globalization;
using Helpdesk.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

/// <summary>
/// The persistence context for the helpdesk.
/// </summary>
/// <remarks>
/// Two mapping decisions here are deliberate and worth being able to justify:
/// <list type="bullet">
/// <item>
/// Enums are stored as text rather than integers. The database is inspected directly during marking
/// and defect investigation, and a status column reading "InProgress" needs no lookup table; the
/// small storage cost is irrelevant at this scale.
/// </item>
/// <item>
/// Every instant is stored as an ISO-8601 UTC string through <see cref="UtcInstant"/>. SQLite has no
/// native date type, and EF Core's default representation for <see cref="DateTimeOffset"/> keeps the
/// original offset — which sorts incorrectly once records span a daylight-saving change. Normalising
/// to UTC first makes ordering and range filtering correct, which matters because nearly every query
/// in this system is a time comparison.
/// </item>
/// </list>
/// </remarks>
public class HelpdeskDbContext : DbContext
{
    private const string InstantFormat = "yyyy-MM-ddTHH:mm:ss.fffffffZ";

    public HelpdeskDbContext(DbContextOptions<HelpdeskDbContext> options) : base(options)
    {
    }

    public DbSet<Ticket> Tickets => Set<Ticket>();

    public DbSet<TicketEvent> TicketEvents => Set<TicketEvent>();

    public DbSet<HoldPeriod> HoldPeriods => Set<HoldPeriod>();

    public DbSet<UserAccount> Users => Set<UserAccount>();

    public DbSet<UserAuditEntry> UserAuditEntries => Set<UserAuditEntry>();

    /// <summary>Round-trips a <see cref="DateTimeOffset"/> through a sortable UTC string.</summary>
    public static readonly ValueConverter<DateTimeOffset, string> UtcInstant = new(
        value => value.ToUniversalTime().ToString(InstantFormat, CultureInfo.InvariantCulture),
        value => DateTimeOffset.ParseExact(
            value, InstantFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal));

    /// <summary>Nullable counterpart of <see cref="UtcInstant"/>.</summary>
    public static readonly ValueConverter<DateTimeOffset?, string?> NullableUtcInstant = new(
        value => value == null
            ? null
            : value.Value.ToUniversalTime().ToString(InstantFormat, CultureInfo.InvariantCulture),
        value => value == null
            ? null
            : DateTimeOffset.ParseExact(
                value, InstantFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<UserAccount>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Id).HasMaxLength(64);
            entity.Property(u => u.DisplayName).IsRequired().HasMaxLength(120);
            entity.Property(u => u.Department).HasMaxLength(120);
            entity.Property(u => u.Role).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(u => u.Role);
        });

        modelBuilder.Entity<UserAuditEntry>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.SubjectUserId).IsRequired().HasMaxLength(64);
            entity.Property(e => e.SubjectDisplayName).HasMaxLength(120);
            entity.Property(e => e.ActorId).IsRequired().HasMaxLength(64);
            entity.Property(e => e.ActorDisplayName).HasMaxLength(120);
            entity.Property(e => e.Reason).HasMaxLength(500);

            entity.Property(e => e.EventType).HasConversion<string>().HasMaxLength(32);
            entity.Property(e => e.FromRole).HasConversion<string>().HasMaxLength(32);
            entity.Property(e => e.ToRole).HasConversion<string>().HasMaxLength(32);

            entity.Property(e => e.OccurredAt).HasConversion(UtcInstant).HasMaxLength(33);

            entity.HasIndex(e => e.SubjectUserId);
            entity.HasIndex(e => e.OccurredAt);
        });

        modelBuilder.Entity<Ticket>(entity =>
        {
            entity.HasKey(t => t.Id);

            entity.Property(t => t.Reference).IsRequired().HasMaxLength(20);
            entity.HasIndex(t => t.Reference).IsUnique();

            entity.Property(t => t.Title).IsRequired().HasMaxLength(200);
            entity.Property(t => t.Description).IsRequired().HasMaxLength(4000);
            entity.Property(t => t.Category).IsRequired().HasMaxLength(80);
            entity.Property(t => t.RequesterId).IsRequired().HasMaxLength(64);
            entity.Property(t => t.RequesterName).IsRequired().HasMaxLength(120);
            entity.Property(t => t.Department).HasMaxLength(120);
            entity.Property(t => t.AssignedTechnicianId).HasMaxLength(64);
            entity.Property(t => t.AssignedTechnicianName).HasMaxLength(120);
            entity.Property(t => t.ResolutionNotes).HasMaxLength(4000);

            entity.Property(t => t.Priority).HasConversion<string>().HasMaxLength(20);
            entity.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(t => t.Sensitivity).HasConversion<string>().HasMaxLength(20);

            entity.Property(t => t.CreatedAt).HasConversion(UtcInstant).HasMaxLength(33);
            entity.Property(t => t.FirstRespondedAt).HasConversion(NullableUtcInstant).HasMaxLength(33);
            entity.Property(t => t.ResolvedAt).HasConversion(NullableUtcInstant).HasMaxLength(33);
            entity.Property(t => t.ClosedAt).HasConversion(NullableUtcInstant).HasMaxLength(33);

            // The queue is filtered and sorted on these constantly, and the performance requirement
            // is stated against a realistic data volume.
            entity.HasIndex(t => t.Status);
            entity.HasIndex(t => t.Priority);
            entity.HasIndex(t => t.CreatedAt);
            entity.HasIndex(t => t.AssignedTechnicianId);
            entity.HasIndex(t => t.RequesterId);
            entity.HasIndex(t => new { t.Status, t.Priority });

            entity.HasMany(t => t.Events)
                  .WithOne()
                  .HasForeignKey(e => e.TicketId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(t => t.HoldPeriods)
                  .WithOne()
                  .HasForeignKey(h => h.TicketId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.Ignore(t => t.IsOpen);
            entity.Ignore(t => t.RequiresWork);
            entity.Ignore(t => t.IsOnHold);
        });

        modelBuilder.Entity<TicketEvent>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.ActorId).IsRequired().HasMaxLength(64);
            entity.Property(e => e.ActorName).HasMaxLength(120);
            entity.Property(e => e.Detail).HasMaxLength(1000);

            entity.Property(e => e.EventType).HasConversion<string>().HasMaxLength(32);
            entity.Property(e => e.ActorRole).HasConversion<string>().HasMaxLength(32);
            entity.Property(e => e.FromStatus).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.ToStatus).HasConversion<string>().HasMaxLength(20);

            entity.Property(e => e.OccurredAt).HasConversion(UtcInstant).HasMaxLength(33);

            entity.HasIndex(e => e.TicketId);
            entity.HasIndex(e => e.OccurredAt);
        });

        modelBuilder.Entity<HoldPeriod>(entity =>
        {
            entity.HasKey(h => h.Id);

            entity.Property(h => h.Reason).HasMaxLength(500);
            entity.Property(h => h.StartedAt).HasConversion(UtcInstant).HasMaxLength(33);
            entity.Property(h => h.EndedAt).HasConversion(NullableUtcInstant).HasMaxLength(33);

            entity.HasIndex(h => h.TicketId);
            entity.Ignore(h => h.IsOpen);
        });
    }
}
