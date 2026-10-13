# Manager-only dashboard

**User story:** As an IT manager, I want manager-only visibility of the dashboard, so that analytics aren't exposed to employees.

| AC | Given / When / Then | Covered by |
|---|---|---|
| AC1 | Given I am logged in as an Employee, when I attempt to view the dashboard, then access is denied. | DASH-B01, B05–B08; DASH-C01–C03, C05, C06 |
| AC2 | Given I am logged in as a Manager, when I view the dashboard, then it displays. | DASH-B01, B09, B10; DASH-C04, C07 |

"Employee" is the `Requester` role (`airport-staff`); "Manager" is `TeamLead` (`it-manager`). The IT Technician is also refused — the story says *manager-only*.

## What changed

- **Server (the real control).** The five dashboard routes (`GET /api/dashboard`, `/api/dashboard/tickets`, `/api/dashboard/export`, `POST`/`DELETE /api/demo/performance-data`) each had their own copy of the role check. They now share one endpoint filter, `RequireManagerFilter` (`src/Helpdesk.Api/Security/RequireManagerFilter.cs`), applied to the route group. A route added to the group later is protected without anyone remembering to copy a check. The rule is an allow-list (`DashboardAccess.IsAllowed` — only `TeamLead`), so a role added to `UserRole` later is refused by default. Anonymous → 401, signed-in non-manager → 403, manager → data with `Cache-Control: no-store`.
- **Client.** `DashboardPage` used to render the dashboard heading, empty summary cards and filters while it waited for the server, and kept the heading and description on screen after a 403. It now shows a neutral loading message, then either the dashboard or only an "Access denied" panel whose heading receives focus. A 403 on any later action (filter, export, demo data) removes figures already on screen.

## Tests

| ID | Level | Where | What | Technique |
|---|---|---|---|---|
| DASH-B01–B03 | Unit | `tests/Helpdesk.Tests/Dashboard/DashboardAccessTests.cs` | Allow-list per role; nobody signed in; unknown role value | Decision table, fail-closed check |
| DASH-B04 | Unit | same | The group contains exactly the five expected routes (stops the other tests passing by testing nothing) | Guard test |
| DASH-B05 | Component (real endpoints + real middleware + real sign-in) | same | Employee **and** technician get 403 on **every** route | Equivalence partitioning (non-manager class) |
| DASH-B06 | Component | same | The 403 body contains no metric names, ticket text or reporter emails | Negative / data-leak check |
| DASH-B07 | Component | same | Employee POST/DELETE of demo data changes nothing | Side-effect check |
| DASH-B08 | Component | same | No token, `X-User-Id`, forged token → 401 on every route | Regression |
| DASH-B09–B10 | Component | same | Manager gets real figures, ticket rows, CSV, and `no-store` | Positive path (AC2) |
| DASH-C01–C02 | Journey (fake API) | `client/src/__tests__/DashboardAccess.test.tsx` | Employee / technician open the dashboard → Access denied, no analytics on screen | Scenario |
| DASH-C03 | Journey | same | A `MutationObserver` records every DOM change between click and refusal — the dashboard layout must never appear, even briefly | Scenario |
| DASH-C04 | Journey | same | Manager lands on the dashboard with server figures, rows and Export | Scenario (AC2) |
| DASH-C05 | Journey | same | Access withdrawn mid-session → figures already shown are removed | State transition |
| DASH-C06–C07 | Component | same | Focus moves to the denial heading; axe-core WCAG 2.1 A/AA scan of both states | Automated accessibility |
| CI | System | `.github/workflows/quality-checks.yml` | Anonymous caller gets 401 from all five routes on the running API | Black-box smoke |

The backend component tests run the real minimal-API endpoints (routing metadata, filter, handlers) behind the real `ActorMiddleware`, with callers signed in through the real `AuthService`. Only the database (EF Core InMemory) and transport (`DefaultHttpContext`) are substituted, so no extra NuGet package is needed.

**Shown to catch the fault:** run against the previous `DashboardPage`, DASH-C01, C02, C03, C05 and C06 fail; with the new page all pass.

## Limitations

- CI runs in Production, where no demo passwords exist, so it can check only the anonymous case; the signed-in employee case is covered by DASH-B05.
- The `Dashboard` nav button is still visible to non-managers (labelled "Manager"). Clicking it is what AC1 describes as "attempting to view". Hiding it would be a usability choice, not a security control.
- axe in jsdom does not judge colour contrast; check the denial panel in a real browser.
