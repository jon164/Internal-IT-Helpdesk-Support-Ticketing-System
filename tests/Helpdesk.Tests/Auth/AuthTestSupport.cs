using Helpdesk.Api.Auth;
using Helpdesk.Core.Domain;
using Helpdesk.Core.Security;
using Helpdesk.Data;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Tests.Auth;

/// <summary>A clock the test controls, so expiry and lockout can be tested without waiting.</summary>
internal sealed class FakeClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
    public void Advance(TimeSpan by) => Now += by;
}

internal static class AuthTestSupport
{
    public const string StaffPassword = "Correct-Horse-1";
    public const string ManagerPassword = "Manager-Pass-2";

    // The minimum iteration count keeps the suite fast. Production uses 600,000;
    // the algorithm and the comparison are identical, only the work factor differs.
    public static PasswordHasher FastHasher() => new(iterations: 1_000);

    public static HelpdeskDbContext NewContext() =>
        new(new DbContextOptionsBuilder<HelpdeskDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    public static async Task<(HelpdeskDbContext Db, AuthService Auth, FakeClock Clock, AuthOptions Options)> CreateAsync()
    {
        var db = NewContext();
        var hasher = FastHasher();
        db.Users.AddRange(
            new UserAccount { Id = "airport-staff", DisplayName = "Airport Staff", Department = "Operations", Role = UserRole.Requester, PasswordHash = hasher.Hash(StaffPassword) },
            new UserAccount { Id = "it-manager", DisplayName = "IT Manager", Department = "IT Services", Role = UserRole.TeamLead, PasswordHash = hasher.Hash(ManagerPassword) },
            new UserAccount { Id = "retired-tech", DisplayName = "Retired", Department = "IT Services", Role = UserRole.Technician, IsActive = false, PasswordHash = hasher.Hash(StaffPassword) },
            new UserAccount { Id = "no-password", DisplayName = "No Password", Department = "IT Services", Role = UserRole.Technician, PasswordHash = null });
        await db.SaveChangesAsync();

        var clock = new FakeClock();
        var options = new AuthOptions();
        return (db, new AuthService(db, hasher, options, clock), clock, options);
    }
}
