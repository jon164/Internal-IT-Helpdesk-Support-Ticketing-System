namespace Helpdesk.Core.Domain;

/// <summary>
/// A person who can use the system.
/// </summary>
/// <remarks>
/// The prototype deliberately stores no credentials. Authentication is out of scope (see the scope
/// boundaries in the problem definition); the running system selects an identity from this table and
/// every authorisation decision is then enforced server-side against the resulting
/// <see cref="ActorContext"/>. Swapping in a real identity provider would replace how an actor is
/// established without changing a single authorisation rule.
/// </remarks>
public class UserAccount
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Department { get; set; } = string.Empty;

    public UserRole Role { get; set; }

    /// <summary>Whether the account is still active. Retained so deactivation can be demonstrated.</summary>
    public bool IsActive { get; set; } = true;

    public ActorContext ToActor() => new(Id, Role, DisplayName);
}
