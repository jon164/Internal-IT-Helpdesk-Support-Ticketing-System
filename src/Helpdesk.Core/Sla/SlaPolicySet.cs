namespace Helpdesk.Core.Sla;

using Helpdesk.Core.Domain;

/// <summary>
/// The complete set of priority definitions and their targets.
/// </summary>
/// <remarks>
/// <para>
/// One of the quality requirements is that priority definitions and target times be changeable without
/// a code change. This type is therefore constructed from configuration at start-up and validated on
/// construction, so a malformed set fails loudly at boot rather than silently producing wrong SLA
/// figures in production reporting.
/// </para>
/// <para>
/// <see cref="Default"/> exists so tests and the seeder have a known-good set to work from; it is not
/// the source of truth at runtime.
/// </para>
/// </remarks>
public sealed class SlaPolicySet
{
    private readonly Dictionary<TicketPriority, SlaPolicy> _policies;

    private SlaPolicySet(Dictionary<TicketPriority, SlaPolicy> policies) => _policies = policies;

    public IReadOnlyCollection<SlaPolicy> Policies => _policies.Values;

    public SlaPolicy this[TicketPriority priority] => For(priority);

    public SlaPolicy For(TicketPriority priority) =>
        _policies.TryGetValue(priority, out var policy)
            ? policy
            : throw new KeyNotFoundException(
                $"No SLA policy is configured for priority {priority}. " +
                "All four priority bands must be present in configuration.");

    /// <summary>
    /// Builds a validated set. Throws <see cref="SlaConfigurationException"/> listing every problem
    /// found if the supplied policies are incomplete, duplicated, or internally inconsistent.
    /// </summary>
    public static SlaPolicySet Create(IEnumerable<SlaPolicy> policies)
    {
        ArgumentNullException.ThrowIfNull(policies);

        var supplied = policies.ToList();
        var problems = new List<string>();

        var duplicates = supplied
            .GroupBy(p => p.Priority)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        foreach (var duplicate in duplicates)
        {
            problems.Add($"{duplicate}: more than one policy supplied for this priority.");
        }

        foreach (var required in Enum.GetValues<TicketPriority>())
        {
            if (supplied.All(p => p.Priority != required))
            {
                problems.Add($"{required}: no policy supplied for this priority.");
            }
        }

        foreach (var problem in supplied.SelectMany(p => p.Validate()))
        {
            problems.Add(problem);
        }

        if (problems.Count > 0)
        {
            throw new SlaConfigurationException(problems);
        }

        return new SlaPolicySet(supplied.ToDictionary(p => p.Priority));
    }

    /// <summary>
    /// The agreed target set for the prototype.
    /// </summary>
    /// <remarks>
    /// P1 and P2 run on a continuous clock because the faults they describe are tied to flight
    /// operations, which do not stop at 19:00. P3 and P4 run on working time only.
    /// One working day is 720 minutes under the default calendar.
    /// </remarks>
    public static SlaPolicySet Default { get; } = Create(new[]
    {
        new SlaPolicy(
            TicketPriority.Critical,
            "P1 Critical",
            "A fault stopping revenue-generating or time-critical corporate activity — for example a "
            + "concession point-of-sale terminal down during a departure peak.",
            ResponseTargetMinutes: 15,
            ResolutionTargetMinutes: 4 * 60,
            SlaClockType.Continuous),

        new SlaPolicy(
            TicketPriority.High,
            "P2 High",
            "A fault materially degrading a time-bound activity with no practical workaround — for "
            + "example operations briefing room AV failing before a shift handover.",
            ResponseTargetMinutes: 60,
            ResolutionTargetMinutes: 8 * 60,
            SlaClockType.Continuous),

        new SlaPolicy(
            TicketPriority.Standard,
            "P3 Standard",
            "A request or fault affecting normal corporate work where a workaround exists — for "
            + "example access-card provisioning for a new contractor.",
            ResponseTargetMinutes: 4 * 60,
            ResolutionTargetMinutes: 3 * 720,
            SlaClockType.BusinessHours),

        new SlaPolicy(
            TicketPriority.Low,
            "P4 Low",
            "A routine request with no operational impact — for example a standard software install.",
            ResponseTargetMinutes: 720,
            ResolutionTargetMinutes: 10 * 720,
            SlaClockType.BusinessHours)
    });
}

/// <summary>
/// Thrown when the configured SLA policy set is unusable. Carries every problem found so an operator
/// can fix the configuration in one pass.
/// </summary>
public sealed class SlaConfigurationException : Exception
{
    public SlaConfigurationException(IReadOnlyList<string> problems)
        : base("The SLA policy configuration is invalid:" + Environment.NewLine +
               string.Join(Environment.NewLine, problems.Select(p => "  - " + p)))
        => Problems = problems;

    public IReadOnlyList<string> Problems { get; }
}
