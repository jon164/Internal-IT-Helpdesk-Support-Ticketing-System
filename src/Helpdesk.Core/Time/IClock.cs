namespace Helpdesk.Core.Time;

/// <summary>
/// Abstraction over "now".
/// </summary>
/// <remarks>
/// Every SLA rule in this system is a statement about elapsed time, so tests must be able to control
/// the clock. Nothing in the domain calls <see cref="DateTimeOffset.UtcNow"/> directly.
/// </remarks>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>Production implementation, backed by the system clock.</summary>
public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>Test double whose current time is set explicitly and can be advanced.</summary>
public sealed class FixedClock : IClock
{
    public FixedClock(DateTimeOffset now) => UtcNow = now;

    public DateTimeOffset UtcNow { get; private set; }

    public void AdvanceBy(TimeSpan delta) => UtcNow = UtcNow.Add(delta);

    public void SetTo(DateTimeOffset now) => UtcNow = now;
}
