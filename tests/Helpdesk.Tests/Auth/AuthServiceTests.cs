using Helpdesk.Api.Auth;
using Helpdesk.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Tests.Auth;

/// <summary>
/// Security and reliability of sign-in, lockout and sessions. Test design: equivalence
/// partitioning over the kinds of credentials a caller can present, and boundary-value analysis
/// on the three numeric limits (failed-attempt threshold, lockout end, session expiry).
/// </summary>
[TestClass]
public sealed class AuthServiceTests
{
    [TestMethod] // AUTH-B07
    public async Task Login_WithCorrectCredentials_ReturnsTokenAndStoresOnlyItsHash()
    {
        var (db, auth, clock, options) = await AuthTestSupport.CreateAsync();

        var result = await auth.LoginAsync("airport-staff", AuthTestSupport.StaffPassword);

        Assert.AreEqual(LoginStatus.Success, result.Status);
        Assert.IsFalse(string.IsNullOrEmpty(result.Token));
        Assert.AreEqual(UserRole.Requester, result.User!.Role);
        Assert.AreEqual(clock.Now.UtcDateTime + options.SessionLifetime, result.ExpiresAtUtc);

        var stored = await db.UserSessions.SingleAsync();
        Assert.AreNotEqual(result.Token, stored.TokenHash, "The raw token must never be persisted.");
        Assert.AreEqual(AuthService.HashToken(result.Token!), stored.TokenHash);
    }

    [TestMethod] // AUTH-B08
    public async Task Login_UsernameIsTrimmedAndCaseInsensitive()
    {
        var (_, auth, _, _) = await AuthTestSupport.CreateAsync();
        var result = await auth.LoginAsync("  Airport-STAFF ", AuthTestSupport.StaffPassword);
        Assert.AreEqual(LoginStatus.Success, result.Status);
    }

    [TestMethod] // AUTH-B09 — every way of failing looks the same to the caller (no account enumeration)
    [DataRow("airport-staff", "wrong-password", DisplayName = "known user, wrong password")]
    [DataRow("nobody", AuthTestSupport.StaffPassword, DisplayName = "unknown user")]
    [DataRow("retired-tech", AuthTestSupport.StaffPassword, DisplayName = "deactivated account, right password")]
    [DataRow("no-password", "anything", DisplayName = "account with no password set")]
    [DataRow("it-manager", AuthTestSupport.StaffPassword, DisplayName = "another account's password")]
    public async Task Login_EveryFailureReturnsTheSameResult(string username, string password)
    {
        var (db, auth, _, _) = await AuthTestSupport.CreateAsync();

        var result = await auth.LoginAsync(username, password);

        Assert.AreEqual(LoginStatus.InvalidCredentials, result.Status);
        Assert.IsNull(result.Token);
        Assert.IsNull(result.User, "A failed sign-in must not return account details.");
        Assert.AreEqual(0, await db.UserSessions.CountAsync());
    }

    [TestMethod] // AUTH-B10
    [DataRow(null, "x")]
    [DataRow("   ", "x")]
    [DataRow("airport-staff", null)]
    [DataRow("airport-staff", "")]
    public async Task Login_MissingUsernameOrPassword_IsRejectedBeforeAnyLookup(string? username, string? password)
    {
        var (db, auth, _, _) = await AuthTestSupport.CreateAsync();

        var result = await auth.LoginAsync(username, password);

        Assert.AreEqual(LoginStatus.MissingCredentials, result.Status);
        Assert.AreEqual(0, (await db.Users.FirstAsync(u => u.Id == "airport-staff")).FailedLoginCount, "A blank submission is not a guess and must not count towards lockout.");
    }

    [TestMethod] // AUTH-B11 — boundary: just below (4), on (5) and just above (6) the threshold
    public async Task Login_LocksAccountOnTheFifthConsecutiveFailure()
    {
        var (_, auth, _, options) = await AuthTestSupport.CreateAsync();

        for (var attempt = 1; attempt < options.MaxFailedAttempts; attempt++)
        {
            var failed = await auth.LoginAsync("airport-staff", "wrong");
            Assert.AreEqual(LoginStatus.InvalidCredentials, failed.Status, $"Attempt {attempt} should not lock yet.");
        }

        var fifth = await auth.LoginAsync("airport-staff", "wrong");
        Assert.AreEqual(LoginStatus.LockedOut, fifth.Status);

        var sixth = await auth.LoginAsync("airport-staff", "wrong");
        Assert.AreEqual(LoginStatus.LockedOut, sixth.Status);

        var rightPasswordWhileLocked = await auth.LoginAsync("airport-staff", AuthTestSupport.StaffPassword);
        Assert.AreEqual(LoginStatus.LockedOut, rightPasswordWhileLocked.Status, "The correct password must not bypass a lockout.");
        Assert.IsNull(rightPasswordWhileLocked.Token);
    }

    [TestMethod] // AUTH-B12 — boundary: just before, exactly at, and (implicitly) after the lockout end
    public async Task Login_LockoutExpiresExactlyAtItsEndTime()
    {
        var (_, auth, clock, options) = await AuthTestSupport.CreateAsync();
        for (var i = 0; i < options.MaxFailedAttempts; i++)
        {
            await auth.LoginAsync("airport-staff", "wrong");
        }

        clock.Advance(options.LockoutDuration - TimeSpan.FromSeconds(1));
        Assert.AreEqual(LoginStatus.LockedOut, (await auth.LoginAsync("airport-staff", AuthTestSupport.StaffPassword)).Status);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.AreEqual(LoginStatus.Success, (await auth.LoginAsync("airport-staff", AuthTestSupport.StaffPassword)).Status);
    }

    [TestMethod] // AUTH-B13 — the count is of *consecutive* failures
    public async Task Login_SuccessResetsTheFailureCount()
    {
        var (_, auth, _, options) = await AuthTestSupport.CreateAsync();

        for (var i = 0; i < options.MaxFailedAttempts - 1; i++)
        {
            await auth.LoginAsync("airport-staff", "wrong");
        }
        Assert.AreEqual(LoginStatus.Success, (await auth.LoginAsync("airport-staff", AuthTestSupport.StaffPassword)).Status);

        for (var i = 0; i < options.MaxFailedAttempts - 1; i++)
        {
            Assert.AreEqual(LoginStatus.InvalidCredentials, (await auth.LoginAsync("airport-staff", "wrong")).Status);
        }
    }

    [TestMethod] // AUTH-B14 — one account's lockout does not affect another
    public async Task Login_LockoutIsPerAccount()
    {
        var (_, auth, _, options) = await AuthTestSupport.CreateAsync();
        for (var i = 0; i < options.MaxFailedAttempts; i++)
        {
            await auth.LoginAsync("airport-staff", "wrong");
        }

        Assert.AreEqual(LoginStatus.Success, (await auth.LoginAsync("it-manager", AuthTestSupport.ManagerPassword)).Status);
    }

    [TestMethod] // AUTH-B15
    public async Task Validate_ReturnsTheAccountForALiveToken()
    {
        var (_, auth, _, _) = await AuthTestSupport.CreateAsync();
        var login = await auth.LoginAsync("it-manager", AuthTestSupport.ManagerPassword);

        var user = await auth.ValidateAsync(login.Token);

        Assert.IsNotNull(user);
        Assert.AreEqual("it-manager", user.Id);
        Assert.AreEqual(UserRole.TeamLead, user.Role);
    }

    [TestMethod] // AUTH-B16 — boundary on session expiry: just below, on, just above
    public async Task Validate_SessionIsValidUntilButNotAtItsExpiry()
    {
        var (_, auth, clock, options) = await AuthTestSupport.CreateAsync();
        var login = await auth.LoginAsync("airport-staff", AuthTestSupport.StaffPassword);

        clock.Advance(options.SessionLifetime - TimeSpan.FromSeconds(1));
        Assert.IsNotNull(await auth.ValidateAsync(login.Token), "One second before expiry the session is still live.");

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.IsNull(await auth.ValidateAsync(login.Token), "At the expiry instant the session is dead.");

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.IsNull(await auth.ValidateAsync(login.Token), "Just after expiry it stays dead.");
    }

    [TestMethod] // AUTH-B17
    [DataRow((string?)null)]
    [DataRow("")]
    [DataRow("not-a-real-token")]
    public async Task Validate_RejectsUnknownOrBlankTokens(string? token)
    {
        var (_, auth, _, _) = await AuthTestSupport.CreateAsync();
        await auth.LoginAsync("airport-staff", AuthTestSupport.StaffPassword);
        Assert.IsNull(await auth.ValidateAsync(token));
    }

    [TestMethod] // AUTH-B18 — oversized input is refused without being hashed
    public async Task Validate_RejectsOversizedToken()
    {
        var (_, auth, _, _) = await AuthTestSupport.CreateAsync();
        Assert.IsNull(await auth.ValidateAsync(new string('a', AuthService.MaxTokenLength + 1)));
    }

    [TestMethod] // AUTH-B19
    public async Task Logout_RevokesTheSession_AndIsIdempotent()
    {
        var (_, auth, _, _) = await AuthTestSupport.CreateAsync();
        var login = await auth.LoginAsync("airport-staff", AuthTestSupport.StaffPassword);

        Assert.IsTrue(await auth.LogoutAsync(login.Token));
        Assert.IsNull(await auth.ValidateAsync(login.Token), "A token must stop working once its session is signed out.");
        Assert.IsFalse(await auth.LogoutAsync(login.Token), "Signing out twice reports nothing left to revoke, without error.");
    }

    [TestMethod] // AUTH-B20 — logging out one session leaves the user's other sessions alone
    public async Task Logout_OnlyAffectsTheGivenSession()
    {
        var (_, auth, _, _) = await AuthTestSupport.CreateAsync();
        var desk = await auth.LoginAsync("airport-staff", AuthTestSupport.StaffPassword);
        var tablet = await auth.LoginAsync("airport-staff", AuthTestSupport.StaffPassword);

        await auth.LogoutAsync(desk.Token);

        Assert.IsNull(await auth.ValidateAsync(desk.Token));
        Assert.IsNotNull(await auth.ValidateAsync(tablet.Token));
        Assert.AreNotEqual(desk.Token, tablet.Token);
    }

    [TestMethod] // AUTH-B21 — deactivating an account ends its existing sessions immediately
    public async Task Validate_RejectsSessionOfADeactivatedAccount()
    {
        var (db, auth, _, _) = await AuthTestSupport.CreateAsync();
        var login = await auth.LoginAsync("airport-staff", AuthTestSupport.StaffPassword);

        var user = await db.Users.FirstAsync(u => u.Id == "airport-staff");
        user.IsActive = false;
        await db.SaveChangesAsync();

        Assert.IsNull(await auth.ValidateAsync(login.Token));
    }

    [TestMethod] // AUTH-B22
    public async Task Login_RemovesTheAccountsExpiredAndRevokedSessions()
    {
        var (db, auth, clock, options) = await AuthTestSupport.CreateAsync();
        var revoked = await auth.LoginAsync("airport-staff", AuthTestSupport.StaffPassword);
        await auth.LogoutAsync(revoked.Token);
        await auth.LoginAsync("airport-staff", AuthTestSupport.StaffPassword);
        clock.Advance(options.SessionLifetime);

        await auth.LoginAsync("airport-staff", AuthTestSupport.StaffPassword);

        Assert.AreEqual(1, await db.UserSessions.CountAsync());
    }

    [TestMethod] // AUTH-B23
    public void CreateToken_IsLongAndUnique()
    {
        var tokens = Enumerable.Range(0, 200).Select(_ => AuthService.CreateToken()).ToList();
        Assert.AreEqual(200, tokens.Distinct().Count());
        Assert.IsTrue(tokens.All(t => t.Length >= 43), "32 random bytes encode to 43 base64url characters.");
    }
}
