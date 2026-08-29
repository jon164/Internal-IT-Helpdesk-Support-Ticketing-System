# US-DASH-01 — Manager service-health dashboard

> **As an IT manager, I want a dashboard summarising ticket activity, so that I can assess service
> health at a glance without reading individual tickets.**

## Acceptance criteria

| ID | Criterion |
|---|---|
| AC-1 | The dashboard reports response and resolution SLA attainment for a selectable reporting period. |
| AC-2 | The dashboard reports open backlog, and how much of it has breached or is close to breaching. |
| AC-3 | The dashboard reports demand broken down by priority and by category. |
| AC-4 | Every figure is obtained without opening an individual ticket. |
| AC-5 | Only the service manager may view the report; other roles are refused **by the server**, not by hiding the page. |
| AC-6 | Changing the reporting period recalculates every figure. |
| AC-7 | Where nothing qualifies for a measure, the dashboard distinguishes "no data" from "zero". |
| AC-8 | Every figure is available as text, not only as a chart. |

## Preconditions

1. `dotnet restore && dotnet build` completes without error.
2. API running: `dotnet run --project src/Helpdesk.Api` — first run seeds 500 sample tickets.
3. Client running: `cd client && npm install && npm run dev`.
4. Browser at <http://localhost:5173>. The role switcher opens on **Maia Thornton — Service manager**.

The seeded dataset is generated from a fixed random seed, so the figures below are reproducible on
any machine. Record the actual values on first run and use them as the expected values thereafter;
they change only if the seeder changes.

---

## Automated tests

```bash
cd client
npm run test
```

22 tests across two files.

| Test file | Covers |
|---|---|
| `src/utils/format.test.ts` | Duration and percentage formatting, the no-data dash, attainment and breach banding |
| `src/pages/DashboardPage.test.tsx` | Rendering of the headline figure, below-target flagging, the no-data case, breach count, the server refusal, category breakdown, table view |

Server-side figures are covered separately by the API and domain suites.

---

## Manual test cases

### TC-D01 — Headline attainment is visible without opening a ticket
**Covers:** AC-1, AC-4
**Steps**

1. Open the dashboard as the service manager.

**Expected**

- "Resolution attainment" is the largest figure on the page, shown as a percentage.
- "Response attainment" is shown beside it.
- Both carry a meter with the 90% target marked on it.
- No ticket has been opened to reach any of this.

---

### TC-D02 — Attainment below target is flagged, not merely displayed
**Covers:** AC-1
**Steps**

1. Observe the badge under each attainment figure.

**Expected**

- Attainment at or above 90% shows "On target" with a tick.
- Attainment between 80% and 90% shows "Watch".
- Attainment below 80% shows "Action needed".
- The badge carries a glyph **and** a word — the state is never conveyed by colour alone.

---

### TC-D03 — Backlog risk is quantified
**Covers:** AC-2
**Steps**

1. Read the "Breached and still open" tile.
2. Compare against the "What is at risk right now" panel.

**Expected**

- The tile's figure equals the "Breached" bar in the panel.
- Breached + At risk + On track equals the "Open backlog" tile.
- When "At risk" is zero, its bar draws **nothing** — no sliver of colour.

---

### TC-D04 — Demand breakdown is present and ordered
**Covers:** AC-3
**Steps**

1. Read "Raised by priority" and "Raised by category".

**Expected**

- All four priority bands appear, labelled P1–P4, shaded as one ordered colour ramp rather than four unrelated colours.
- Categories are sorted largest first.
- Hovering any bar shows the count and its share of the period total.
- Category totals sum to the "Raised" figure in the "Raised / resolved" tile.

---

### TC-D05 — Reporting period drives every figure
**Covers:** AC-6
**Steps**

1. Note the figures on "Last 30 days".
2. Click "Last 7 days".
3. Click "Last 90 days".

**Expected**

- The date range under "Service health" changes to match.
- Raised, resolved, attainment and both breakdowns all change.
- Open backlog and breach counts are point-in-time and may legitimately stay the same — they describe the queue now, not the period.

---

### TC-D06 — A technician is refused by the server
**Covers:** AC-5
**Priority:** High — this is the security case.
**Steps**

1. Switch "Viewing as" to **Nikau Ashford — Technician**.
2. Open the browser developer tools, Network tab.
3. Observe both the page and the request to `/api/reports/summary`.

**Expected**

- The page shows "Not available to your role" and the server's own message.
- The network request returns **HTTP 403**, not 200 with a hidden page.
- No attainment figures appear anywhere in the response body.

**Why this matters:** it demonstrates that access control is enforced server-side. A test that only
checked the page looked empty would pass against a purely cosmetic restriction.

---

### TC-D07 — The refusal cannot be bypassed from the client
**Covers:** AC-5
**Steps**

1. With the API running, issue the request directly:

```powershell
curl.exe -i -H "X-User-Id: tech-nikau" "http://localhost:5099/api/reports/summary"
curl.exe -i -H "X-User-Id: user-01"    "http://localhost:5099/api/reports/summary"
curl.exe -i                            "http://localhost:5099/api/reports/summary"
curl.exe -i -H "X-User-Id: lead-maia"  "http://localhost:5099/api/reports/summary"
```

**Expected**

| Caller | Status |
|---|---|
| Technician | 403 Forbidden |
| Requester | 403 Forbidden |
| No identity | 401 Unauthorized |
| Service manager | 200 OK with the report |

The header names an identity only — the role comes from the database record, so no header value
grants access that the account does not already have.

---

### TC-D08 — "No data" is distinguished from "zero"
**Covers:** AC-7
**Steps**

1. Select "Last 7 days".
2. If any measure has nothing qualifying in that window, observe how it renders.

**Expected**

- A measure with no qualifying tickets renders **—**, never "0.0%".
- The hint text explains why, e.g. "Nothing was resolved in this period."

**Why this matters:** reporting 0% attainment for a quiet week would be indefensible to airport
management — it states that everything failed when in fact nothing was measurable.

---

### TC-D09 — Every figure is available as text
**Covers:** AC-8
**Steps**

1. Click "View as table".

**Expected**

- An "All figures" table appears listing every headline metric plus the full priority and category breakdowns.
- Values match the charts above exactly.
- The table is usable with a screen reader and can be copied into the report.

---

### TC-D10 — The layout survives a narrow viewport
**Covers:** usability / NFR
**Steps**

1. Reduce the browser window to roughly 390px wide, or use device emulation.

**Expected**

- The page does not scroll horizontally.
- Panels stack to a single column.
- Category labels remain readable rather than truncating to ellipses.

---

## Result record

| Test case | Date | Tester | Result | Evidence | Notes |
|---|---|---|---|---|---|
| TC-D01 | | | | | |
| TC-D02 | | | | | |
| TC-D03 | | | | | |
| TC-D04 | | | | | |
| TC-D05 | | | | | |
| TC-D06 | | | | | |
| TC-D07 | | | | | |
| TC-D08 | | | | | |
| TC-D09 | | | | | |
| TC-D10 | | | | | |

Attach a screenshot for TC-D01, TC-D06 and TC-D09 — those three carry the story, the security
control, and the accessibility provision respectively.
