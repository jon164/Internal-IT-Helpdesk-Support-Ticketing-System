using System.Security.Cryptography;
using System.Text;
using Helpdesk.Core.Domain;
using Helpdesk.Core.Security;
using Helpdesk.Data;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Api.Auth;

public enum LoginStatus
{
    Success,
    MissingCredentials,
    InvalidCredentials,
    LockedOut,
}

public sealed record LoginResult(
    LoginStatus Status,
    string? Token = null,
    DateTime? ExpiresAtUtc = null,
    UserAccount? User = null,
    DateTime? LockoutEndUtc = null);

/// <summary>
/// Credential checking, account lockout and server-side sessions. All rules live here rather
/// than in the endpoints so they can be unit-tested without an HTTP host.
/// </summary>
public sealed class AuthService
{
    /// <summary>Longest token accepted. Real tokens are 43 characters; anything far longer is refused before hashing.</summary>
    public const int MaxTokenLength = 256;

    private readonly HelpdeskDbContext _db;
    private readonly PasswordHasher _hasher;
    private readonly AuthOptions _options;
    private readonly TimeProvider _clock;

    public AuthService(HelpdeskDbContext db, PasswordHasher hasher, AuthOptions options, TimeProvider clock)
    {
        _db = db;
        _hasher = hasher;
        _options = options;
        _clock = clock;
    }

    private DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;

    /// <summary>Usernames are compared trimmed and case-insensitively; stored ids are lower case.</summary>
    public static string NormaliseUsername(string? username) => (username ?? string.Empty).Trim().ToLowerInvariant();

    public async Task<LoginResult> LoginAsync(string? username, string? password, CancellationToken ct = default)
    {
        var id = NormaliseUsername(username);
        if (id.Length == 0 || string.IsNullOrEmpty(password))
        {
            return new LoginResult(LoginStatus.MissingCredentials);
        }

        var now = UtcNow;
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

        // Unknown, deactivated and password-less accounts get the same answer as a wrong
        // password, after the same amount of hashing work, so a caller cannot tell them apart.
        if (user is null || !user.IsActive || string.IsNullOrEmpty(user.PasswordHash))
        {
            _hasher.VerifyAgainstDummy(password);
            return new LoginResult(LoginStatus.InvalidCredentials);
        }

        if (user.LockoutEndUtc is { } lockedUntil)
        {
            if (lockedUntil > now)
            {
                return new LoginResult(LoginStatus.LockedOut, LockoutEndUtc: lockedUntil);
            }

            user.LockoutEndUtc = null;
            user.FailedLoginCount = 0;
        }

        if (!_hasher.Verify(password, user.PasswordHash))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= _options.MaxFailedAttempts)
            {
                user.FailedLoginCount = 0;
                user.LockoutEndUtc = now + _options.LockoutDuration;
                await _db.SaveChangesAsync(ct);
                return new LoginResult(LoginStatus.LockedOut, LockoutEndUtc: user.LockoutEndUtc);
            }

            await _db.SaveChangesAsync(ct);
            return new LoginResult(LoginStatus.InvalidCredentials);
        }

        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;

        // Tidy this account's dead sessions so the table does not grow without bound.
        var stale = await _db.UserSessions
            .Where(s => s.UserId == user.Id && (s.RevokedAtUtc != null || s.ExpiresAtUtc <= now))
            .ToListAsync(ct);
        _db.UserSessions.RemoveRange(stale);

        var token = CreateToken();
        var session = new UserSession
        {
            UserId = user.Id,
            TokenHash = HashToken(token),
            CreatedAtUtc = now,
            ExpiresAtUtc = now + _options.SessionLifetime,
        };

        _db.UserSessions.Add(session);
        await _db.SaveChangesAsync(ct);

        return new LoginResult(LoginStatus.Success, token, session.ExpiresAtUtc, user);
    }

    /// <summary>Returns the signed-in account for a bearer token, or null if the token is unknown, expired, revoked or its account is deactivated.</summary>
    public async Task<UserAccount?> ValidateAsync(string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > MaxTokenLength)
        {
            return null;
        }

        var hash = HashToken(token);
        var session = await _db.UserSessions
            .AsNoTracking()
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.TokenHash == hash, ct);

        if (session?.User is null || !session.IsValidAt(UtcNow) || !session.User.IsActive)
        {
            return null;
        }

        return session.User;
    }

    /// <summary>Revokes the session. Returns false if there was no live session for the token. Safe to call twice.</summary>
    public async Task<bool> LogoutAsync(string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > MaxTokenLength)
        {
            return false;
        }

        var hash = HashToken(token);
        var session = await _db.UserSessions.FirstOrDefaultAsync(s => s.TokenHash == hash, ct);
        if (session is null || session.RevokedAtUtc is not null)
        {
            return false;
        }

        session.RevokedAtUtc = UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>256 bits from the OS cryptographic RNG, base64url-encoded.</summary>
    public static string CreateToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
