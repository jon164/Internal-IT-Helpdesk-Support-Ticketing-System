using Helpdesk.Data.Seeding;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Tests.Auth;

/// <summary>Security of the default configuration: no environment gets a usable password by accident.</summary>
[TestClass]
public sealed class DemoAccountSeedingTests
{
    [TestMethod] // AUTH-B29
    public async Task WithoutConfiguredPasswords_AccountsExistButCannotSignIn()
    {
        await using var db = AuthTestSupport.NewContext();

        await DbSeeder.EnsureDemoUsersAsync(db, demoPasswords: null, AuthTestSupport.FastHasher());

        var users = await db.Users.ToListAsync();
        Assert.AreEqual(3, users.Count);
        Assert.IsTrue(users.All(u => u.PasswordHash is null));
    }

    [TestMethod] // AUTH-B30
    public async Task WithConfiguredPasswords_StoresHashesThatVerify()
    {
        await using var db = AuthTestSupport.NewContext();
        var hasher = AuthTestSupport.FastHasher();
        var passwords = new Dictionary<string, string> { ["it-manager"] = "Configured-1" };

        await DbSeeder.EnsureDemoUsersAsync(db, passwords, hasher);

        var manager = await db.Users.SingleAsync(u => u.Id == "it-manager");
        Assert.AreNotEqual("Configured-1", manager.PasswordHash);
        Assert.IsTrue(hasher.Verify("Configured-1", manager.PasswordHash));
        Assert.IsNull((await db.Users.SingleAsync(u => u.Id == "airport-staff")).PasswordHash, "Accounts not named in configuration stay password-less.");
    }

    [TestMethod] // AUTH-B31 — re-running start-up must not reset a password that has been set
    public async Task Reseeding_DoesNotOverwriteAnExistingPassword()
    {
        await using var db = AuthTestSupport.NewContext();
        var hasher = AuthTestSupport.FastHasher();
        await DbSeeder.EnsureDemoUsersAsync(db, new Dictionary<string, string> { ["it-manager"] = "First-1" }, hasher);

        await DbSeeder.EnsureDemoUsersAsync(db, new Dictionary<string, string> { ["it-manager"] = "Second-2" }, hasher);

        var manager = await db.Users.SingleAsync(u => u.Id == "it-manager");
        Assert.IsTrue(hasher.Verify("First-1", manager.PasswordHash));
        Assert.AreEqual(3, await db.Users.CountAsync());
    }
}
