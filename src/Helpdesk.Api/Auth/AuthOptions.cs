namespace Helpdesk.Api.Auth;

/// <summary>Bound from the "Authentication" configuration section.</summary>
public sealed class AuthOptions
{
    /// <summary>Absolute session lifetime. Eight hours covers one help-desk shift.</summary>
    public double SessionHours { get; set; } = 8;

    /// <summary>Consecutive wrong passwords allowed before the account is locked.</summary>
    public int MaxFailedAttempts { get; set; } = 5;

    /// <summary>How long a locked account refuses sign-in.</summary>
    public double LockoutMinutes { get; set; } = 15;

    public TimeSpan SessionLifetime => TimeSpan.FromHours(SessionHours);
    public TimeSpan LockoutDuration => TimeSpan.FromMinutes(LockoutMinutes);
}
