# Task 3 — Test Execution and Final Test Results

**Generated:** 2026-10-11T20:21:14+13:00  
**Target:** Internal IT Helpdesk Support Ticketing System  
**Scope:** Backend workflow rules, SQLite persistence, HTTP/API acceptance and authorization checks, and browser ticket-submission journeys.

## Summary

| Measure | Result |
|---|---:|
| Planned checks | 45 |
| Executed checks | 42 |
| Passed | 39 |
| Failed | 3 |
| Blocked | 0 |
| Not run | 3 |

The 24-test backend suite and 13 API acceptance checks passed. Two unauthenticated API checks failed because reporter email and internal-note data were returned to callers without role authorization. The frontend Vitest command also exited non-zero because the client contains no test files. Three additional quality checks were not run; these are listed below rather than treated as passes.

**Release decision: Not ready for release.** The anonymous disclosure of reporter details and internal notes conflicts with the role-visibility requirements in the technician plan. The client test suite is also not established, and performance, automated accessibility, and a complete technician journey remain unverified.

## Test approach and environment

The test levels follow the earlier test strategy: unit tests for domain/workflow rules; SQLite integration for relational write/read persistence; API checks for HTTP behavior and server-side role restrictions; browser checks for user-visible ticket submission, keyboard operation, and theme switching; and regression by running the full backend suite after the persistence correction.

| Capability | Observed result |
|---|---|
| Docker | **Unavailable.** `docker info` exited 1: `failed to connect to the docker API at npipe:////./pipe/dockerDesktopLinuxEngine ... The system cannot find the file specified.` |
| Node.js / npm | **Available:** Node v22.18.0, npm 11.5.2. |
| Browser | **Available:** VS Code integrated Playwright-driven browser used against Vite at `http://127.0.0.1:5173/`. The project-local Playwright CLI is not installed. |
| Infrastructure tier | **Fallback:** EF Core InMemory for isolated service tests and SQLite in-memory for the relational persistence test. The API smoke tests used an isolated local API/SQLite instance, not the repository's existing untracked database. Docker unavailability limits fidelity versus a containerized deployment database. |
| Startup | **PASS.** Started the ASP.NET Core API on port 5099 and the Vite client on port 5173. `GET /api/technicians` returned HTTP 200. Startup elapsed time was not captured. |

## Final test register

### Backend automated tests — 24 passed

Command:

```powershell
dotnet test tests\Helpdesk.Tests\Helpdesk.Tests.csproj --no-restore --logger "console;verbosity=normal"
```

**Result:** exit code 0; 24 passed, 0 failed, 0 skipped.

| ID | Test | Result |
|---|---|---|
| B01 | `GetTicketsAsync_OrdersNewestFirst_WhenSortIsNewest` | PASS |
| B02 | `GetTicketsAsync_OrdersByPriorityThenAge_WhenSortIsPriorityAge` | PASS |
| B03 | `GetTicketsAsync_OrdersOldestFirst_WhenSortIsOldest` | PASS |
| B04 | `GetTicketAsync_ThrowsNotFound_WhenTicketDoesNotExist` | PASS |
| B05 | `ClaimAsync_RejectsInactiveTechnician` | PASS |
| B06 | `ClaimAsync_AssignsTicketAndWritesAuditEntry` | PASS |
| B07 | `ClaimAsync_RejectsTicketAlreadyAssigned` | PASS |
| B08 | `ClaimAsync_RejectsMissingTicket` | PASS |
| B09 | `ClaimAsync_RejectsMissingTechnician` | PASS |
| B10 | `ReassignAsync_AssignsActiveTechnicianAndAuditsChange` | PASS |
| B11 | `ReassignAsync_RejectsInactiveTechnicianAndListsAvailableTechnicians` | PASS |
| B12 | `ChangeStatusAsync_ValidTransitionSetsPendingTimestampAndAudit` | PASS |
| B13 | `ChangeStatusAsync_RejectsInvalidTransition` | PASS |
| B14 | `EmployeeCommentAsync_AddsPublicNoteAndReturnsPendingTicketToProgress` | PASS |
| B15 | `AddNoteAsync_PersistsInternalNote` | PASS |
| B16 | `AddNoteAsync_RejectsBlankNote` | PASS |
| B17 | `LinkAsync_CreatesLinkToExistingTicket` | PASS |
| B18 | `LinkAsync_RejectsSelfLink` | PASS |
| B19 | `LinkAsync_RejectsDuplicateLinkInReverseDirection` | PASS |
| B20 | `CloseAsync_ClosesResolvedTicketAndRecordsReason` | PASS |
| B21 | `CloseAsync_RejectsTicketThatIsNotResolved` | PASS |
| B22 | `AutoCloseAsync_ClosesOnlyResolvedTicketsOlderThanTimeout` | PASS |
| B23 | `GetUnassignedTicketsAsync_ExcludesAssignedAndClosedTickets` | PASS |
| B24 | `SqliteIntegration_AddNotePersistsAcrossDbContexts` | PASS |

### API acceptance and smoke checks — 13 passed

The checks used a running API and included success and expected-error responses. Ticket and note persistence checks wrote data and read it back through HTTP.

| ID | Check | Expected / observed | Result |
|---|---|---|---|
| A01 | List active technicians | HTTP 200; at least three returned | PASS |
| A02 | Sign in with valid demo user | HTTP 200 | PASS |
| A03 | Sign in with unknown user | HTTP 401 | PASS |
| A04 | Manager dashboard without identity | HTTP 401 | PASS |
| A05 | Manager dashboard as requester | HTTP 403 | PASS |
| A06 | Manager dashboard as manager | HTTP 200 | PASS |
| A07 | Create a ticket | Success response, “Ticket created.” | PASS |
| A08 | Read created ticket from ticket list | Created ticket present | PASS |
| A09 | Submit a blank note | HTTP 400 | PASS |
| A10 | Submit a valid internal note | Success response with note body | PASS |
| A11 | Read the persisted note back | Note present | PASS |
| A12 | Flight lookup in offline mode | HTTP 400 with validation response | PASS |
| A13 | Read a nonexistent ticket | HTTP 404 | PASS |

### Browser system checks — 2 passed

| ID | Journey | Evidence | Result |
|---|---|---|---|
| W01 | Airport staff completes the ticket form; ticket creation succeeds and the new ticket appears in the live queue/details panel. | Browser showed “Ticket created.”, updated ticket count, and the submitted description. | PASS |
| W02 | Keyboard user submits a ticket using Enter; theme is switched light → dark. | Browser assertions confirmed the created ticket was visible and the theme-toggle labels changed in both directions. | PASS |

The standard pointer-click action repeatedly timed out while waiting for the submit button to become stable. A forced Playwright click and a keyboard Enter submission both completed successfully. Treat pointer interaction as a browser-tool limitation requiring confirmation in a local browser runner, not as proof that ordinary pointer use is defect-free.

### Security checks — 2 failed

The expected outcome was denial or redaction for an unauthenticated caller, consistent with role restrictions and reporter/internal-note visibility in the prior test plan.

| ID | Check | Expected | Observed | Result |
|---|---|---|---|---|
| S01 | Anonymous `GET /api/tickets` reporter-data visibility | Deny access or omit reporter contact details | HTTP 200 response included a reporter email address | **FAIL — privacy exposure** |
| S02 | Anonymous `GET /api/tickets/{ticketId}/notes` internal-note visibility | Deny access or omit internal notes | HTTP 200 response included an internal note body and `isInternal: true` | **FAIL — internal information exposure** |

These are release-blocking findings. Add server-side authentication/authorization and role-based response filtering, then rerun these exact checks as regression tests. Hiding data in the client is not an adequate fix.

### Frontend test availability — 1 failed check

| ID | Check | Result |
|---|---|---|
| F01 | `npx vitest run` in `client` | **FAIL — exit code 1.** Vitest 4.1.10 ran but reported “No test files found.” `client/package.json` also has no `test` script. No frontend automated assertions ran. |

## Failure, correction, and regression history

The first expanded backend run reported 14 passed and 10 failed. The failures shared an EF Core `DbUpdateConcurrencyException`: newly created audit entries, notes, and links had nonempty client-generated GUID keys, so EF treated new child records attached through an already-tracked ticket as existing rows and attempted updates.

The correction removed eager GUID initializers from `TicketAuditEntry.Id`, `TicketNote.Id`, and `TicketLink.Id`, allowing EF to generate keys as it tracks the new dependents. The full backend suite was then rerun and passed 24/24. The separate SQLite test verified a note could be written using one `DbContext` and read using another. The original 10 failures are therefore recorded as a discovered-and-corrected defect, not as failures in the final backend run.

## Build, lint, and observed quality risks

- Client production build: **PASS** (`npm run build`, exit code 0).
- Client lint: **PASS with warnings** (`npm run lint`, exit code 0); three existing React Hook dependency warnings remain in `TicketDetails.tsx`, `DashboardPage.tsx`, and `TicketsPage.tsx`.
- NuGet restore/build output reported **NU1903** for `SQLitePCLRaw.lib.e_sqlite3` 2.1.11, advisory `GHSA-2m69-gcr7-jv3q`. This dependency warning was not remediated as part of Task 3 and requires a separate security review.
- The browser console reported the configured `3270NerdFontMono-Regular.ttf` could not be decoded (`invalid sfntVersion`); the browser rendered using a fallback font. Confirm the font asset and visual appearance on the target system.
- No performance/load benchmark or automated WCAG/contrast scan was run. Browser inspection confirmed labels and keyboard submission for the tested form, but does not establish full accessibility conformance.

## Planned but not run

| Check | Status | Reason / gap |
|---|---|---|
| Complete technician end-to-end journey (sign in, claim, update, resolve/close, verify role-specific data) | NOT RUN | No repeatable authenticated technician browser journey was completed; the demo role selector is not evidence of secure server-side identity. |
| Automated accessibility/WCAG audit, including measured contrast | NOT RUN | No accessibility audit tooling was run. Manual browser inspection is limited evidence only. |
| Performance/load test | NOT RUN | No workload target or performance test harness was available in this task. |

## Quality metrics and final verdict

- Backend automated test pass rate: **100% (24/24)** on final run.
- API acceptance check pass rate: **100% (13/13)** for the tested functional/expected-error cases.
- Browser flow pass rate: **100% (2/2)** for the flows actually asserted.
- Security privacy checks: **0% (0/2)**; both unauthenticated disclosure checks failed.
- Requirement evidence: technician workflow and persistence behavior have service-level evidence; the tested manager authorization cases have API evidence. Full three-role system acceptance and complete role-specific visibility remain unverified.
- Frontend automated test coverage: **not established**; Vitest exits 1 because no test files are present.

```yaml
environment:
  docker: UNAVAILABLE — docker info exit 1; Docker Desktop Linux engine named pipe was not found
  node: AVAILABLE — v22.18.0; npm 11.5.2
  playwright: AVAILABLE — integrated browser used; local project Playwright CLI not installed
  infra-tier: FALLBACK(embedded/in-memory) — Docker daemon unavailable; SQLite in-memory persistence test plus isolated local SQLite API
  browser-tier: PRIMARY(Playwright) — integrated browser used for two asserted UI flows; pointer click stability needs local-runner confirmation
startup: PASS — ASP.NET Core API on port 5099; GET /api/technicians returned 200; startup time not captured
integration: PASS — exit_code: 0, passed: 24, failed: 0, skipped: 0; workflow/service tests and SQLite note round-trip; no containerized database
e2e: PARTIAL — exit_code: 0 for the API smoke harness; 13 API checks and 2 browser flows passed; 2 privacy checks failed; full technician journey not run
overall: FAIL — release-blocking anonymous disclosure of reporter and internal-note data; frontend test command also exits 1 with no tests
```

**Final release recommendation: Not ready.** Resolve S01/S02 with server-side authorization and redaction, add/routinely execute frontend tests, then rerun the affected security checks and the full regression suite. Complete the planned accessibility, performance, and technician-journey checks before reconsidering release.
