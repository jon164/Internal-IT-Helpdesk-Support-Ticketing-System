namespace Helpdesk.Core.Domain;

public class UserAccount
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// PBKDF2 hash in the format produced by <see cref="Security.PasswordHasher"/>.
    /// Null means the account has no password and cannot sign in.
    /// The plain-text password is never stored.
    /// </summary>
    public string? PasswordHash { get; set; }

    /// <summary>Consecutive failed sign-in attempts since the last success or lockout.</summary>
    public int FailedLoginCount { get; set; }

    /// <summary>While this is in the future, the account refuses sign-in even with the right password.</summary>
    public DateTime? LockoutEndUtc { get; set; }
}
