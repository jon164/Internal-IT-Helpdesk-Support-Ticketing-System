# Ticket filters (status, priority, assignee)

Engineering notes for the IT manager filter story. Repository notes for the team, not report text.

> **User story:** As an IT manager, I want to filter tickets by status, priority, and assignee,
> so that I can investigate bottlenecks or a technician's workload.
>
> - **AC1** Given a status filter is applied, when I search, then only tickets with that status are returned.
> - **AC2** Given no tickets match the filter, when I search, then an empty result is shown gracefully.

## Starting point

The manager dashboard already had Status / Priority / Assignee dropdowns and a
`GET /api/dashboard/tickets` route. Reviewing it against the acceptance criteria found four defects:

| # | Defect | Effect |
|---|---|---|
| 1 | An unknown status (for example `?status=Bogus`) was turned into `New`. | Breaks AC1: a typo returned New tickets instead of an error. |
| 2 | `assignee=Unassigned` was compared with technician names. | Choosing "Unassigned" always showed nothing. |
| 3 | The Assignee list was built from the tickets already on screen. | It shrank when another filter was applied, and a technician with no tickets (zero workload) could never be picked. |
| 4 | The filter code was copied into the table route and the CSV route, and both loaded every ticket before filtering. | The two could drift apart; slower with large backlogs. |

An unknown priority was also silently ignored, returning every ticket.

## What changed

**Server**

- New `src/Helpdesk.Core/Domain/TicketFilter.cs`: one place that parses the query values and applies them.
  - `All`, blank or missing = no filter on that field.
  - Status names are the ones the UI uses (`New`, `InProgress`, `WaitingOnUser`, `Resolved`, `Closed`); priority is `Low`, `Medium`, `High`, `Critical`. Case and surrounding spaces are ignored.
  - `Unassigned` = tickets with no technician; any other assignee value is a technician name.
  - Unknown status or priority is rejected. Numbers such as `3` are not accepted as enum shortcuts.
- `DashboardEndpoints.cs`: `/api/dashboard/tickets` and `/api/dashboard/export` both use `TicketFilter`.
  - The filter is applied in the database query, so only matching tickets are loaded.
  - No match → `200` with `[]` (AC2).
  - Invalid value → `400 { error: "InvalidFilter", message }`. The bad value is echoed back cut to 40 characters.
  - The manager-only check still runs first, so a non-manager gets `403` whatever the filter.

**Client** (`client/src/pages/DashboardPage.tsx`)

- The Assignee list comes from `GET /api/technicians` (plus any names in the results, plus `Unassigned`), so it never shrinks and includes technicians with no tickets. It is fetched only after the server has allowed the dashboard.
- Empty result: "No tickets match these filters." with a hint, instead of the old developer note. A separate message is shown when there are simply no tickets at all.
- A **Clear filters** button appears whenever a filter is set.
- The result count ("2 results") is a polite live region, so screen readers announce it when filters change.

## Testing

### Quality areas

| Area | Why it matters here |
|---|---|
| **Reliability** (primary) | AC1 and AC2 are both correctness claims. Defects 1 and 2 were wrong results returned with no error, the most damaging kind for someone investigating workload. |
| **Usability / accessibility** | AC2 says "gracefully". A manager must understand an empty result and get back from it; a screen-reader user must hear the count change. |
| **Security** (secondary) | Filters must not get round the manager-only rule, and bad input must not be reflected back in full. |

### Techniques and why

- **Equivalence partitioning.** Filter values have endless spellings but few behaviour classes: each valid status, each valid priority, a named technician, `Unassigned`, a technician with no tickets, "All"/blank, and invalid. One test per class covers them without testing every string.
- **Oracle from the seed data.** Expected counts are calculated separately with plain LINQ from the seed list, and each test checks both "nothing extra" and "nothing missing". A test cannot pass just because the filter and the expectation share a bug.
- **Regression tests** pin defects 1, 2 and 3 so they cannot come back.
- **API tests through the real pipeline** (real routing, manager filter, sign-in; EF Core InMemory database), the same harness approach as the dashboard access tests.
- **Journey tests with a fake server** that really filters its rows by the query string, so the client tests check both what the page sends and what it shows.
- **axe-core WCAG 2.1 A/AA scan** on the empty state. (Colour contrast is not checked by axe in jsdom.)

### Test register

Backend — `tests/Helpdesk.Tests/Dashboard/TicketFilterTests.cs` (MSTest)

| ID | Checks | AC / area |
|---|---|---|
| FILT-B01 | Each of the 5 statuses returns only and all tickets with that status | AC1 · reliability |
| FILT-B02 | Each of the 4 priorities returns only and all tickets with that priority | reliability |
| FILT-B03 | Assignee by name, ignoring case and spaces | reliability |
| FILT-B04 | `Unassigned` returns only unassigned tickets (regression, defect 2) | reliability |
| FILT-B05 | Status + priority + assignee combine with AND | reliability |
| FILT-B06 | No match returns an empty list, no exception | AC2 · reliability |
| FILT-B07 | Technician with no tickets returns an empty list | AC2 · reliability |
| FILT-B08 | `All`, `all`, blank, spaces and missing mean no filter | reliability |
| FILT-B09 | Unknown status rejected, including `4`, `-1`, `New,Closed` (regression, defect 1) | AC1 · reliability, security |
| FILT-B10 | Unknown priority rejected | reliability |
| FILT-B11 | A 500-character bad value is cut short in the error | security |
| FILT-B12 | API: `?status=Resolved` returns only Resolved rows | AC1 · reliability |
| FILT-B13 | API: no match is `200` with `[]` | AC2 · reliability |
| FILT-B14 | API: `?assignee=Unassigned` works | reliability |
| FILT-B15 | API: unknown status is `400` with a message and no ticket data | reliability, security |
| FILT-B16 | API: CSV export returns the same tickets as the table | reliability |
| FILT-B17 | API: a non-manager with filters (valid or invalid) still gets `403` and no data | security |

Client — `client/src/__tests__/DashboardFilters.test.tsx` (Vitest)

| ID | Checks | AC / area |
|---|---|---|
| FILT-C01 | Choosing a status sends `status=Resolved` and shows only those tickets | AC1 · reliability |
| FILT-C02 | All three filters are sent together | reliability |
| FILT-C03 | No match shows the empty message, `0 results`, no error; Clear filters brings everything back | AC2 · usability |
| FILT-C04 | A technician with no tickets can be picked and shows the empty message | AC2 · usability |
| FILT-C05 | The Assignee list keeps every technician after another filter narrows results (regression, defect 3) | usability |
| FILT-C06 | The result count is a polite live region and updates | accessibility |
| FILT-C07 | Empty state passes the axe WCAG 2.1 A/AA scan | accessibility |

### Results

- Client: `npm test` — 50/50 passing (run in a Linux copy of the client on 12 Oct 2026). `tsc -b` clean.
- Backend: `dotnet test` — 128/128 passing, 0 failed, 0 skipped (Jon's Windows machine, .NET SDK 10.0.401, 12 Oct 2026). This total includes the new FILT-B tests.
