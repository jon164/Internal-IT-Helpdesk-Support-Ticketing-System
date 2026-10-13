namespace Helpdesk.Core.Domain;

/// <summary>
/// A signed-in session. The client holds a random bearer token; the database holds only
/// its SHA-256 hash, so a copy of the database does not yield usable sessions.
/// </summary>
public class UserSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }

    public UserAccount? User { get; set; }

    public bool IsValidAt(DateTime nowUtc) => RevokedAtUtc is null && nowUtc < ExpiresAtUtc;
}
