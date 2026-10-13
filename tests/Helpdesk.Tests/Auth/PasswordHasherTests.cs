using Helpdesk.Core.Security;

namespace Helpdesk.Tests.Auth;

/// <summary>Security: how passwords are stored and compared.</summary>
[TestClass]
public sealed class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = AuthTestSupport.FastHasher();

    [TestMethod] // AUTH-B01
    public void Verify_AcceptsTheOriginalPassword()
    {
        var hash = _hasher.Hash("S3cret!pass");
        Assert.IsTrue(_hasher.Verify("S3cret!pass", hash));
    }

    [TestMethod] // AUTH-B02 — near-miss inputs are a separate equivalence class from "unrelated"
    [DataRow("s3cret!pass")]   // case differs
    [DataRow("S3cret!pas")]    // one character short
    [DataRow("S3cret!pass ")]  // trailing space is not trimmed
    [DataRow("")]
    public void Verify_RejectsAnyOtherPassword(string attempt)
    {
        var hash = _hasher.Hash("S3cret!pass");
        Assert.IsFalse(_hasher.Verify(attempt, hash));
    }

    [TestMethod] // AUTH-B03
    public void Hash_NeverContainsThePlainText_AndIsSaltedPerCall()
    {
        var first = _hasher.Hash("S3cret!pass");
        var second = _hasher.Hash("S3cret!pass");

        Assert.IsFalse(first.Contains("S3cret!pass"));
        Assert.AreNotEqual(first, second, "Two hashes of one password must differ, or identical passwords are visible in the database.");
        Assert.IsTrue(_hasher.Verify("S3cret!pass", second));
    }

    [TestMethod] // AUTH-B04 — corrupt stored values must fail closed, not throw
    [DataRow((string?)null)]
    [DataRow("")]
    [DataRow("plaintext-password")]
    [DataRow("PBKDF2-SHA256$abc$AAAA$AAAA")]
    [DataRow("PBKDF2-SHA256$1000$not-base64!$AAAA")]
    [DataRow("MD5$1000$AAAA$AAAA")]
    [DataRow("PBKDF2-SHA256$999999999$AAAA$AAAA")]
    public void Verify_ReturnsFalseForMalformedStoredHash(string? stored)
    {
        Assert.IsFalse(_hasher.Verify("anything", stored));
    }

    [TestMethod] // AUTH-B05 — the work factor is recorded, so it can be raised later
    public void Hash_RecordsIterationCount_AndDefaultMeetsOwaspFigure()
    {
        Assert.IsTrue(_hasher.Hash("x").StartsWith("PBKDF2-SHA256$1000$", StringComparison.Ordinal));
        Assert.IsTrue(PasswordHasher.DefaultIterations >= 600_000);
    }

    [TestMethod] // AUTH-B06
    public void VerifyAgainstDummy_AlwaysReturnsFalse()
    {
        Assert.IsFalse(_hasher.VerifyAgainstDummy("anything"));
    }
}
