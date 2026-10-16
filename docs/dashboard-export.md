# Dashboard export (summary, by priority, ticket list)

Engineering notes for the IT manager export story. Repository notes for the team, not report text.

> **User story:** As an IT manager, I want to export the dashboard summary, so that I can include
> service data in reports to senior management.
>
> - **AC1** Given the dashboard is displayed, when I choose export, then a file (e.g. CSV) of the current metrics is produced.
> - **AC2** Given no ticket data exists, when I export, then a valid file with headers and zero rows is produced.

## Starting point

The dashboard already had an **Export current CSV** button calling `GET /api/dashboard/export`.
Reviewing it against the story found:

| # | Defect | Effect |
|---|---|---|
| 1 | The only file was the ticket list. None of the summary figures (backlog, average resolution, overdue, recurring issue) could be exported. | Breaks AC1: "current metrics" were not in any file. |
| 2 | Fields were joined with commas and never quoted. | A reporter called "Lee, Ana" split into two columns and shifted the rest of the row. |
| 3 | Reporter names went into the file unchanged. | A name such as `=HYPERLINK(...)` runs as a formula when the file is opened in Excel (CSV / formula injection). Reporter names are typed by employees, so this is untrusted input. |
| 4 | Line endings came from `Environment.NewLine`, no byte-order mark. | LF-only files when served from Linux; Excel could misread non-English names. |
| 5 | With no tickets, the recurring-issue card said "Incident (0)". | A made-up value that would have gone into the summary file. |

## Decision: three files, chosen when exporting

AC2 (header and zero rows) fits a file with one row per item, but a plain "metric, value" summary
always has a row per figure, even when every figure is 0. Jon's decision (15 Oct 2026): keep the
original ticket list and let the manager choose the file when exporting:

| Choice | Route | Rows | No tickets |
|---|---|---|---|
| **Metric summary** (default) | `GET /api/dashboard/export/summary` | One per dashboard figure: `metric,value,unit` | 8 rows: counts 0, average and category blank (not "0 minutes", not "Incident") |
| **Breakdown by priority** | `GET /api/dashboard/export/by-priority` | One per priority that has tickets, most urgent first | Header only (AC2) |
| **Ticket list** (original) | `GET /api/dashboard/export` + filters | One per ticket, current table filters | Header only (AC2) |

- The summary and by-priority files cover every ticket, the same as the summary cards. Only the ticket list uses the table filters. The panel says which.
- The summary file always has the same rows in the same order (including "Report generated (UTC)"), so a report template can rely on row positions.
- File names are dated in New Zealand time, e.g. `airport-helpdesk-summary-2026-10-15.csv`.

## What changed

**Server**

- `src/Helpdesk.Core/Reporting/DashboardSummary.cs`: one place that works out the dashboard figures. The cards (`GET /api/dashboard`), the summary file and the by-priority file all use it, so a report cannot disagree with the screen. Definitions are unchanged from the cards.
- `src/Helpdesk.Core/Reporting/Csv.cs`: RFC 4180 quoting, formula guard (apostrophe before text starting `= + - @`, tab or CR, but not before plain numbers such as `-3`), CRLF, UTF-8 BOM, invariant-culture numbers. All three files go through it.
- `src/Helpdesk.Core/Reporting/DashboardExport.cs`: the summary and by-priority file layouts and the file name.
- `DashboardEndpoints.cs`: two new routes in the manager-only group (so the same 401/403 rules apply); the ticket list now uses `Csv`; the recurring-issue card says "None" when there are no tickets.
- CI smoke check: anonymous calls to the two new routes must get 401.

**Client**

- `client/src/components/ExportPanel.tsx`: **Export current CSV** now opens a panel with three radio choices and a short description of each. Focus goes to the first choice on open and back to the Export button on close (Escape, Cancel, or after a download). `aria-expanded` on the button.
- `client/src/services/api.ts`: `exportDashboard(kind, filters, role)`. Uses the server's file name only if it is a plain name (no path), otherwise a dated fallback. A 401 signs the user out like other requests. The object URL is revoked after the click, not in the same tick.
- A failed download keeps the panel open with the error so it can be retried; a 403 removes the dashboard as before.

## Testing

### Quality areas

| Area | Why it matters here |
|---|---|
| **Reliability** (primary) | The file goes to senior management. A shifted column or a figure that differs from the dashboard is wrong data with no visible error. |
| **Security** | Defect 3 (formula injection from employee-typed names); the new routes must be manager-only; a header must not steer the download path. |
| **Compatibility** | The file is opened in Excel on Windows: quoting, CRLF, BOM and decimal points must work regardless of the server's OS or culture. |
| **Usability / accessibility** | Choosing a file must work by keyboard and with a screen reader, and focus must not be lost when the panel closes. |

### Techniques and why

- **Equivalence partitioning** on CSV fields: plain text, text needing quotes (comma, quote, CR, LF), formula-like text, plain numbers. Few behaviour classes, endless inputs.
- **Boundary values:** formula guard on `-2+3` (guarded) against `-3` (a number, not guarded); file-name date at 23:59:59 and 00:00:00 NZDT; no tickets (AC2).
- **Independent oracle:** expected figures are worked out by hand from a five-ticket fixture (documented in the test), and files are read back with a separate RFC 4180 parser written only for the tests, not with `string.Split`. Defect 2 passes a `Split` test but fails this one.
- **Consistency test:** the summary file is compared with the `/api/dashboard` JSON for the same data, and the priority rows must add up to the summary totals.
- **Regression tests** for defects 2, 3 and 5, and for the original ticket-list export with filters.
- **Client journey tests** with the fake server, keyboard-only test, and an axe-core WCAG 2.1 A/AA scan with the panel open.

### Test register

Backend (MSTest, `tests/Helpdesk.Tests/Dashboard/DashboardExportTests.cs`):

| ID | Level | What it checks | Area |
|---|---|---|---|
| EXP-B01 | Unit | Plain text and null written unchanged / empty | Reliability |
| EXP-B02 | Unit (4 rows) | Comma, quote, LF, CRLF are quoted; quotes doubled | Reliability |
| EXP-B03 | Unit (6 rows) | `= + - @`, tab, CR get an apostrophe | Security |
| EXP-B04 | Unit (4 rows) | `-3`, `-0.25`, `+5`, `0` are not changed | Reliability (boundary of B03) |
| EXP-B05 | Unit | No rows → exactly the header line | AC2 |
| EXP-B06 | Unit | UTF-8 BOM, CRLF, non-English names | Compatibility |
| EXP-B07 | Unit | Numbers use `.` under a `de-DE` culture | Compatibility |
| EXP-B08 | Unit | Missing number → blank cell | Reliability |
| EXP-B09 | Unit | Row with wrong field count throws | Maintainability |
| EXP-B10 | Unit | No tickets → zeros, null average and category, no priority rows | AC2 |
| EXP-B11 | Unit | Mixed fixture → hand-worked summary | AC1 |
| EXP-B12 | Unit | By priority: order, skipped priorities, per-row figures | AC1 |
| EXP-B13 | Unit | Priority rows add up to the summary | Consistency |
| EXP-B14 | Unit (2 rows) | File-name date either side of NZ midnight | Boundary |
| EXP-B15 | Endpoint | Each choice → 200, `text/csv`, attachment name, BOM | AC1 |
| EXP-B16 | Endpoint | No tickets → every file valid; ticket and priority files header-only | AC2 |
| EXP-B17 | Endpoint | Summary file matches the `/api/dashboard` cards | AC1, consistency |
| EXP-B18 | Endpoint | By-priority file rows for the fixture | AC1 |
| EXP-B19 | Endpoint (3 rows) | Formula, comma and quotes in a reporter name stay in one inert cell | Security, regression |
| EXP-B20 | Endpoint | No tickets → recurring issue "None" | Regression |
| EXP-B21 | Endpoint | Employee gets 403 and no CSV on all three routes | Security |

Changed existing tests: DASH-B04 and DASH-B06 now list the two new routes (DASH-B05 then checks
403 on them automatically).

Client (Vitest, `client/src/__tests__/DashboardExport.test.tsx`):

| ID | What it checks | Area |
|---|---|---|
| EXP-C01 | Export opens the panel with three choices; Metric summary selected and focused; nothing downloaded yet | Usability |
| EXP-C02 | Metric summary → `/export/summary`, bearer token, no filters, saved under the server's name, notice | AC1 |
| EXP-C03 | Breakdown by priority → `/export/by-priority` | AC1 |
| EXP-C04 | Ticket list still offered and still sends the table filters | Regression |
| EXP-C05 | No tickets → header-only file downloaded, reported as success | AC2 |
| EXP-C06 | Escape and Cancel close the panel; focus returns to Export | Accessibility |
| EXP-C07 | Whole export by keyboard only | Accessibility |
| EXP-C08 | axe-core WCAG 2.1 A/AA scan with the panel open | Accessibility |
| EXP-C09 | Failed download keeps the panel open, shows the error, retry works | Reliability |
| EXP-C10 | 403 on export removes the dashboard figures | Security |
| EXP-C11 | Server file name used only if it is a plain name | Security |

Also fixed: FILT-C04 was flaky (it picked "Priya Shah" before the technician roster had loaded;
failed 2 of 4 runs on the unchanged code). It now waits for the option first; 5/5 runs passed.

## Execution status

- **Client: 61/61 passing** (15 Oct 2026, Linux copy of the client on Jon's machine, Node, `npx vitest run`): the previous 50 plus 11 new. `npm run build` passes; `npm run lint` has 0 errors (3 warnings, all pre-existing).
- **Backend: not yet run.** No .NET SDK was available where these changes were made. Expected total if the previous 155 still pass: 190 (35 new runs). Do not cite backend results until `dotnet test` has run.

## Known limitations

- The summary and by-priority files ignore the table filters, by design (they match the cards).
- The ticket `id` column is still a hash of the ticket's GUID, as on screen; it is not a stable ticket number.
- Colour contrast of the panel is not checked by axe in jsdom; it needs a real-browser check.
- "Open backlog" on the card is described as "Every ticket not Closed" but also excludes Resolved tickets (unchanged; the file uses the same rule).
