# SLA breach flagging (overdue metric)

Engineering notes for the IT manager SLA story. Repository notes for the team, not report text.

> **User story:** As an IT manager, I want tickets breaching their priority SLA flagged,
> so that I can intervene before service levels slip.
>
> - **AC1** Given a Critical ticket open longer than its SLA window, when I view the overdue metric, then it is counted as overdue.
> - **AC2** Given a ticket within its SLA window, when I view the metric, then it is not counted.

## Starting point

The dashboard already had an **Overdue** card ("Outside priority SLA"), a per-row SLA column, an
`slaOverdue` CSV column and an "SLA overdue" chip in the ticket queue. All four used the same rule,
copied four times:

```csharp
ticket.Priority == TicketPriority.Critical && ticket.Status != Closed && ticket.Status != Resolved
```

There was **no time window at all**:

| # | Defect | Effect |
|---|---|---|
| 1 | Any open Critical ticket counted, however new. | Breaks AC2: a Critical ticket raised one minute ago showed as overdue. |
| 2 | High, Medium and Low tickets could never be overdue. | A High ticket open for three days was invisible to the manager. |
| 3 | The rule was copied into the metric, the table, the CSV export and the ticket queue. | Changing one copy would make the dashboard disagree with itself. |

## SLA windows

From the project's agreed priority policy (resolution targets):

| Priority | Window | Clock |
|---|---|---|
| Critical | 4 hours | Continuous |
| High | 8 hours | Continuous |
| Medium (policy name: Standard) | 3 working days | Business hours |
| Low | 10 working days | Business hours |

Business hours: Monday-Friday 07:00-19:00 New Zealand time (one working day = 12 hours).

## What changed

**Server**

- New `src/Helpdesk.Core/Domain/SlaPolicy.cs`, the only place that decides "overdue":
  - Only open tickets (New, InProgress, WaitingOnUser) can be overdue. Resolved and Closed never count.
  - "Open longer than" is strict: exactly on the window is still within SLA, and one second past it is overdue.
  - Medium and Low run on business hours. Nights and weekends are off the clock, and daylight-saving changes don't add or lose time.
  - A creation time in the future counts as zero elapsed time.
  - Times read back from SQLite (`Kind = Unspecified`) are treated as UTC, which is how the app stores them.
  - An unknown priority value gets the strictest (Critical) window, so it shows on the dashboard rather than being hidden.
- `DashboardEndpoints.cs`: the metric, the table rows and the CSV export call `SlaPolicy.IsOverdue`. The current time comes from the injected `TimeProvider`, so tests can set the clock.
- `TicketEndpoints.cs`: the ticket queue's `slaOverdue` uses the same rule.

**Client:** no change needed. The Overdue card, SLA column and queue chip already display the server's value.

## Testing

### Quality areas

| Area | Why it matters here |
|---|---|
| **Reliability** (primary) | The metric is only useful if it is right. A false "overdue" wastes the manager's attention; a missed breach is the failure the story exists to stop. The old rule did both. |
| **Maintainability** | The rule was copied in four places. Tests now check that the metric, the table and the CSV agree, so a future change in one place fails the build. |

### Techniques and why

- **Boundary-value analysis.** Off-by-one errors on time limits are the likeliest defect (`>` against `>=`, or an hour lost overnight). Each window is tested 1 second inside, exactly on, and 1 second past it.
- **Equivalence partitioning.** Statuses fall into two classes (open, finished) and priorities into four windows. One case per class covers them without testing every combination.
- **Controllable clock.** The policy takes "now" as a parameter, and the endpoints use `TimeProvider` (a `FakeClock` in tests). Boundaries can be hit to the second, and the results don't depend on what day the suite runs.
- **Regression test.** The two cases the old rule got wrong are pinned so they cannot come back.
- **Consistency test.** One mixed set of tickets, three outputs (metric, table, CSV), one expected answer.

### Test register

Backend (MSTest), `tests/Helpdesk.Tests/Dashboard/SlaOverdueTests.cs`:

| ID | What it checks | Technique |
|---|---|---|
| SLA-B01 | Critical at 3h59m59s / 4h / 4h00m01s: within, within, overdue (AC1, AC2) | BVA |
| SLA-B02 | High at 7h59m59s / 8h / 8h00m01s | BVA |
| SLA-B03 | New, InProgress, WaitingOnUser count; Resolved, Closed never do | EP |
| SLA-B04 | Same 5-hour age, judged against each priority's own window | EP |
| SLA-B05 | Medium: on/past 36 business hours; a weeknight adds nothing | BVA |
| SLA-B06 | Low: on/past 10 working days; a weekend adds nothing | BVA |
| SLA-B07 | Business clock: partial days, a weekend-only span, a full day | EP |
| SLA-B08 | Business clock across the 27 Sep 2026 daylight-saving change | Boundary |
| SLA-B09 | Future creation time: zero elapsed, not overdue | Robustness |
| SLA-B10 | SQLite `Unspecified` time treated as UTC | Robustness |
| SLA-B11 | Unknown priority gets the strictest window | Fail-visible |
| SLA-B12 | Endpoint: Critical 4h01m old is counted (AC1) | Acceptance |
| SLA-B13 | Endpoint: Critical 3h59m old is not counted (AC2) | Acceptance |
| SLA-B14 | Endpoint: the same ticket joins the count when the clock passes 4h | BVA + fake clock |
| SLA-B15 | Endpoint: old-rule cases (1-minute Critical, 9-hour High) | Regression |
| SLA-B16 | Endpoint: metric, table and CSV export agree on a mixed set | Consistency |

16 test methods, 27 test runs (data rows counted separately).

Changed existing test: **DASH-B09** seeded a Critical ticket created "now" and expected it to be overdue,
which is the defect this story removes. Its fixture ticket is now 5 hours old, so the assertion (`overdueCount` 1) is unchanged.

### Execution status

**SLA tests: 27/27 passing** (13 Oct 2026, on Jon's Windows machine, .NET 10, run filtered to
`SlaPolicyTests` and `SlaOverdueEndpointTests`, 2.3 s).

**Full suite: not yet run.** An earlier full `dotnet test` run ended when the PC crashed, before any results
were written; the cause is not known. Expected total if the previous 128 still pass: 155. DASH-B09 (the changed
existing test) runs only in the full suite.

## Known limitations

- **Waiting on the employee does not pause the clock.** The project policy says it should. The ticket stores only when the current wait began (`PendingSince`), not the total of earlier waits, so the paused time can't be worked out. Fixing it needs a stored "time on hold" field updated on each status change.
- **Public holidays count as working days** for Medium and Low.
- **The client offers three priorities, the policy has four.** Medium is used for the policy's "Standard". This mismatch was already known.
- **Only the resolution target is checked.** The policy also sets response targets (for example 15 minutes for Critical), which this story does not cover.
- No "at risk" warning (75% of the window used). That would be a separate story.
