# In-scope capability coverage

Where each capability from the Task 1 scope table is implemented, and where it is tested. Use this to
build the requirements traceability matrix — it is the same information, one column short.

| Capability | Domain + API | Tests | GUI | Screen |
|---|---|---|---|---|
| Configurable priority levels with targets | ✅ | ✅ | ✅ | Ticket detail, *Against target*; `appsettings.json` |
| Reporting: volume, throughput, attainment | ✅ | ✅ | ✅ | Performance, Backlog |
| Elapsed-time tracking, warning and breach states | ✅ | ✅ | ✅ | Queue rows and ticket detail, for every role |
| Role-based access control, three roles | ✅ | ✅ | ✅ | Role-specific navigation, per-ticket permissions, refusal pages |
| Request submission and triage | ✅ | ✅ | ✅ | My requests → *Raise a request*; Queue → Triage |
| Assignment and ownership with state transitions | ✅ | ✅ | ✅ | Ticket detail, *What you can do* |
| Complete audit trail | ✅ | ✅ | ✅ | Ticket detail, *History*; Accounts, *Account change history* |

## Acceptance documents

| Story | Document | Roles it serves |
|---|---|---|
| US-DASH-01 | `US-DASH-01-acceptance.md` | Service manager |
| US-BACKLOG-01 | `US-BACKLOG-01-acceptance.md` | Service manager |
| US-ADMIN-01 | `US-ADMIN-01-acceptance.md` | Service manager |
| US-SUBMIT-01 | `US-SUBMIT-01-acceptance.md` | Requester |
| US-QUEUE-01 | `US-QUEUE-01-acceptance.md` | Technician, service manager |
| US-AUDIT-01 | `US-AUDIT-01-acceptance.md` | All three |

## What each role can reach

Navigation is by role, and lands each person on the screen that is theirs.

| Role | Tabs, in order | Lands on |
|---|---|---|
| Requester | My requests | My requests |
| Technician | Queue, My requests | Queue |
| Service manager | Performance, Backlog, Queue, Accounts, My requests | Performance |

Everybody keeps **My requests**, because a technician is also somebody who occasionally needs a new
laptop. Which tabs appear is a convenience: every request behind every one of them is authorised
server-side against the caller's stored role, and each ticket carries its own server-computed
permissions rather than the client deciding what to offer.

## Automated verification

| Suite | Count | What it covers |
|---|---|---|
| Domain | 95 | SLA calculation, business calendar, state machine, access policy, metrics |
| Persistence and services | 73 | EF Core mapping, authorisation → workflow → persistence → audit, query performance at 500 tickets |
| API over HTTP | 95 | Routing, every role guard, status-code mapping, the demonstration-data routes |
| Client | 92 | Every screen, including each refusal path |

The .NET figures come from package-free harnesses run outside the repository, because nuget.org is
unreachable from the build container used to develop this. **The MSTest suite that Task 6 requires is
still outstanding** and is the largest remaining gap; the harnesses show the assertions exist and are
passing, but they are not the deliverable.

## Defects found by testing

Worth citing in Task 4 — note how they were found, which varies more than the defects themselves.

| # | Defect | Found by |
|---|---|---|
| 1 | Open backlog counted resolved-but-unconfirmed tickets, overstating the queue | Reading the metric's definition against the domain |
| 2 | API rejected `"priority": "High"` — minimal APIs do not accept string enum names | First real HTTP call; it compiled cleanly |
| 3 | Seeded backlog was implausible: 148 open against 84/month intake | Sanity-checking generated data against the scenario |
| 4 | Zero-value bars drew a sliver of colour, reading as a small non-zero count | Rendering the page |
| 5 | `TicketAccessPolicy.CanView` fell through, so a new role would silently gain queue access | Extending the code, not testing it |
| 6 | An empty system reported "0 breached ✓ None outstanding" — every figure right, the impression false | Rendering the page |
| 7 | The category catalogue was derived from existing tickets, so an empty system offered none | Making the application start empty |
| 8 | The technician queue returned 159 rows, almost all closed, then 35 of which 24 were resolved | Rendering the page |

Five of the eight were found by looking at the running system rather than by a test, and two of those
(4 and 6) are cases where every individual figure was correct and the overall impression was wrong.
That is worth a paragraph on its own: it is the class of defect a passing test suite cannot catch,
and it is the argument for manual test cases sitting alongside automated ones rather than being
treated as their poor relation.
