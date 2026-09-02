# US-BACKLOG-01 — Unresolved queue and whether the team is falling behind

> **As an IT manager, I want to see how many tickets are unresolved, so that I can tell whether the
> team is falling behind.**

## Scope note

The count alone does not satisfy this story. "Falling behind" is a statement about direction: 24
unresolved tickets is healthy if the figure was 40 last month and alarming if it was 12. The
Performance view already reported a point-in-time backlog; this story adds the history that makes it
interpretable, plus the ageing that says whether the queue is stale or merely busy.

## Acceptance criteria

| ID | Criterion |
|---|---|
| AC-1 | The dashboard reports how many tickets are currently unresolved. |
| AC-2 | It states whether that number is growing, steady or shrinking, against where it started. |
| AC-3 | It shows the unresolved count at the close of each week across a selectable window. |
| AC-4 | It shows tickets raised against tickets resolved per week, so the movement can be explained. |
| AC-5 | It reports a clearance rate, and flags it when work is arriving faster than it is cleared. |
| AC-6 | It shows how long unresolved work has been waiting, and names the longest-waiting ticket. |
| AC-7 | It shows how unresolved work is distributed across technicians, including unassigned work. |
| AC-8 | "Unresolved" means the same thing here as everywhere else in the system — the two reports never disagree about the size of the queue. |
| AC-9 | Only the service manager may view it; other roles are refused by the server. |
| AC-10 | Every figure is available as text, not only as a chart. |

## Preconditions

As for US-DASH-01: API on `http://localhost:5099`, client on `http://localhost:5173`, signed in as
**Maia Thornton — Service manager**, with the demonstration dataset generated from the **Accounts**
tab. Select the **Backlog** tab.

---

## Automated tests

```bash
cd client
npm run test
```

36 tests across three files. `src/pages/BacklogView.test.tsx` covers this story directly: the
headline count, each direction verdict, the comparison against the window start, clearance-rate
flagging, the ageing breakdown, unassigned work, the no-data case, window selection, the server
refusal, and the table view.

Server-side figures are covered by the domain and API suites, including the reconciliation in AC-8.

---

## Manual test cases

### TC-B01 — The unresolved count leads the view
**Covers:** AC-1
**Steps:** Open the Backlog tab.
**Expected:** "Unresolved right now" is the largest figure on the page, with a whole number.

---

### TC-B02 — Direction of travel is stated, not left to the reader
**Covers:** AC-2
**Steps:** Read the badge under the headline figure, and its supporting line.
**Expected:**

- One of "Queue growing", "Holding steady" or "Queue shrinking".
- Beneath it, the comparison against the start of the window, e.g. "Down 4 from 28 at the start of the window."
- A growing queue is marked as needing action; a shrinking one is marked positively.

---

### TC-B03 — Weekly history is plotted
**Covers:** AC-3
**Steps:** Read "Unresolved queue over time". Hover a point.
**Expected:** One point per week; the tooltip names the week and the unresolved count at its close;
the final point equals the headline figure.

---

### TC-B04 — The movement is explained
**Covers:** AC-4
**Steps:** Read "Raised against resolved". Hover a week where the queue rose.
**Expected:** Two bars per week with a legend; the tooltip gives both counts and states whether the
queue grew or shrank that week; a week whose raised bar is taller corresponds to a rise in the trend
chart above.

---

### TC-B05 — Clearance rate is flagged when the team is losing ground
**Covers:** AC-5
**Steps:** Read the "Clearance rate" tile across all three windows (4, 8, 12 weeks).
**Expected:** A rate at or above 100% shows "On target"; below 100% shows "Arriving faster than
cleared". The rate matches the totals in the flow chart.

---

### TC-B06 — Ageing and the longest wait
**Covers:** AC-6
**Steps:** Read "How long work has been waiting" and the "Longest wait" tile.
**Expected:** Five bands from "Under 1 day" to "Over 2 weeks"; the counts sum to the headline figure;
the tile names a specific ticket reference so it can be chased.

---

### TC-B07 — Distribution across the team
**Covers:** AC-7
**Steps:** Read "Who is holding the queue".
**Expected:** One bar per technician plus an "Unassigned" bar, sorted largest first; the counts sum
to the headline figure. Unassigned work is visually distinguished — it is the manager's to allocate,
not a person's workload.

---

### TC-B08 — The two reports agree
**Covers:** AC-8
**Priority:** High — inconsistent figures make both reports useless.
**Steps:**

1. On the Performance tab, note "Open backlog" and "Breached and still open".
2. Switch to the Backlog tab and note "Unresolved right now" and "Breached and waiting".
3. Change the Backlog window from 8 weeks to 4 and then 12.

**Expected:** The two pairs match exactly, and the queue size does not change with the window — it is
a point-in-time fact, not a property of the reporting period.

Confirm from outside the browser:

```powershell
curl.exe -s -H "X-User-Id: lead-maia" "http://localhost:5099/api/reports/summary"  | findstr openBacklog
curl.exe -s -H "X-User-Id: lead-maia" "http://localhost:5099/api/reports/backlog?weeks=8" | findstr unresolvedNow
```

---

### TC-B09 — The backlog report is restricted
**Covers:** AC-9
**Steps:** Switch "Viewing as" to a technician, with the Network tab open.
**Expected:** "Not available to your role", and the request to `/api/reports/backlog` returns **403**.

```powershell
curl.exe -i -H "X-User-Id: tech-nikau" "http://localhost:5099/api/reports/backlog"
curl.exe -i                            "http://localhost:5099/api/reports/backlog"
curl.exe -i -H "X-User-Id: lead-maia"  "http://localhost:5099/api/reports/backlog?weeks=99"
```

Expected: 403, then 401, then 400 — the last because the window is out of range.

---

### TC-B10 — Every figure available as text
**Covers:** AC-10
**Steps:** Click "View as table".
**Expected:** An "All figures" table including the full weekly series, each age band, the direction
verdict and the clearance rate. Values match the charts.

---

## Result record

| Test case | Date | Tester | Result | Evidence | Notes |
|---|---|---|---|---|---|
| TC-B01 | | | | | |
| TC-B02 | | | | | |
| TC-B03 | | | | | |
| TC-B04 | | | | | |
| TC-B05 | | | | | |
| TC-B06 | | | | | |
| TC-B07 | | | | | |
| TC-B08 | | | | | |
| TC-B09 | | | | | |
| TC-B10 | | | | | |

Screenshot TC-B02, TC-B08 and TC-B09 — the story's core claim, the consistency guarantee, and the
access control.

## Known limitation to declare

Weekly history is reconstructed from each ticket's current timestamps rather than replayed from the
audit trail. A ticket that was resolved and later reopened has its resolution instant cleared, so it
appears never to have been resolved and past weeks can overstate the queue slightly. This is stated
in the view's own footer and in the code. A production implementation would replay the status events.
