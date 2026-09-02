# US-QUEUE-01 — Triage, ownership and state transitions

> **As a support technician, I want a queue of work with clear ownership and states, so that nothing
> is dropped and no two of us work the same ticket.**

## Scope note

The problem definition names duplicated effort and unclear ownership as two of the three failures of
the shared mailbox. This story is the answer to both: every ticket has at most one owner, every
change of state is a named transition the server validates, and both are visible to everybody who can
see the ticket.

The state machine is `New → Triaged → Assigned → In progress → Resolved → Closed`, with `On hold`
reachable from active work and `Cancelled` from anything not yet terminal. It lives in
`TicketWorkflow`; the interface offers whichever moves the server says are legal from where the
ticket currently is, and nothing else.

## Acceptance criteria

| ID | Criterion |
|---|---|
| AC-1 | A technician has a queue of work, ordered most urgent first. |
| AC-2 | The queue defaults to work that still needs a technician, not everything ever assigned. |
| AC-3 | Every row shows its position against the resolution target in words, not colour alone. |
| AC-4 | The queue can be filtered by scope, status and priority, and every filter is applied server-side. |
| AC-5 | An unassigned ticket can be claimed; assignment to somebody else is the service manager's. |
| AC-6 | Only legal transitions are offered, and the server refuses an illegal one independently. |
| AC-7 | Resolving requires resolution notes; placing on hold requires a reason. |
| AC-8 | Re-prioritising requires a written justification and is restricted to the service manager. |
| AC-9 | A restricted ticket is absent from the queue of a technician who does not hold it, and returns 404 if requested directly. |

## Preconditions

API on `http://localhost:5099`, client on `http://localhost:5173`, with the demonstration dataset
generated (Accounts → Demonstration data, as the service manager). Sign in as **Nikau Ashford**.

---

## Automated tests

```bash
cd client
npm run test
```

`QueueView.test.tsx` and `TicketDetailPage.test.tsx` cover this story. Server-side, the domain suite
covers the state machine exhaustively and the API suite covers each guard over HTTP.

---

## Manual test cases

### TC-Q01 — The queue opens on work, not on history
**Covers:** AC-1, AC-2
**Priority:** High — this is a defect found by looking at the screen, not by a test.
**Steps:** Sign in as Nikau Ashford.
**Expected:** **My work** is selected, and it holds only tickets still needing a technician. Switching
to **Everything** shows a far longer list including closed and cancelled work.

*The first build of this screen asked only for `assignedTo` and returned 159 rows, almost all of them
closed. The second added `openOnly` and still buried eleven live tickets under twenty-four that were
resolved and waiting on somebody else to confirm. Resolved work now has its own **Awaiting
confirmation** view. Every figure in all three versions was correct; only the third was usable.*

---

### TC-Q02 — Filters are questions to the server, not sieves in the browser
**Covers:** AC-4
**Steps:** Switch between **My work**, **All open work**, **Unassigned** and **Awaiting
confirmation**, and set Status and Priority.
**Expected:** Each change reloads. On **Awaiting confirmation** the Status control is *disabled* and
shows Resolved, because that view is itself a status filter — a control that silently did nothing
would be worse than one that says why.

```powershell
curl.exe -s -H "X-User-Id: tech-nikau" "http://localhost:5099/api/tickets?needsWorkOnly=true" | ConvertFrom-Json | Measure-Object
curl.exe -s -H "X-User-Id: tech-nikau" "http://localhost:5099/api/tickets?openOnly=true"     | ConvertFrom-Json | Measure-Object
```
**Expected:** `needsWorkOnly` returns strictly fewer, and excludes every Resolved ticket.

*Filtering in the browser would mean the client had already been sent tickets it may not be entitled
to. Filtering server-side means it never receives them.*

---

### TC-Q03 — Ownership is claimed, and cannot be claimed twice
**Covers:** AC-5
**Steps:** Open an unassigned ticket from the **Unassigned** view. Press **Assign to me**. Open it
again.
**Expected:** The ticket now names you as assigned, an **Assigned** entry appears in its history, and
**Assign to me** is gone — the server would still accept it, but it would change nothing, and a
button that does nothing teaches the reader to distrust the rest of them.

As the service manager, the same screen offers **Assign to** with a technician list instead.

---

### TC-Q04 — Only legal moves are offered
**Covers:** AC-6
**Steps:** Open a ticket in **New**. Note the buttons. Triage it, then note them again.
**Expected:** From New the only move is **Triage**. From Triaged, **Mark assigned** and **Cancel**.
From In progress, **Place on hold**, **Resolve** and **Cancel**.

The buttons are an affordance, not the control. Confirm the server refuses an illegal move:

```powershell
curl.exe -i -X POST "http://localhost:5099/api/tickets/1/transition" -H "X-User-Id: tech-nikau" -H "Content-Type: application/json" -d "{\"status\":\"Closed\"}"
```
**Expected:** **409 Conflict**, naming the states involved.

---

### TC-Q05 — Resolving and holding demand an explanation
**Covers:** AC-7
**Steps:** On a ticket in progress, press **Resolve**. Try to confirm with the notes empty, then
write them.
**Expected:** **Confirm: Resolve** stays disabled until notes are written. The same for **Place on
hold** and its reason. Afterwards, the notes appear on the ticket and the reason appears in the
history and in the hold list.

```powershell
curl.exe -i -X POST "http://localhost:5099/api/tickets/1/transition" -H "X-User-Id: tech-nikau" -H "Content-Type: application/json" -d "{\"status\":\"Resolved\"}"
```
**Expected:** **400**, saying resolution notes are required.

Confirm the hold suspends the resolution clock: place a P3 ticket on hold, note the remaining time,
wait, resume, and confirm the remaining time has not moved by the time spent on hold.

---

### TC-Q06 — Re-prioritising is the service manager's, and must be justified
**Covers:** AC-8
**Steps:** As Nikau, open a ticket. As Maia, open the same one.
**Expected:** No priority control for Nikau. For Maia, a priority dropdown and a **Justification**
box, with **Apply** disabled until the justification is written. The change and its reason appear in
the history.

```powershell
curl.exe -i -X POST "http://localhost:5099/api/tickets/1/priority" -H "X-User-Id: tech-nikau" -H "Content-Type: application/json" -d "{\"priority\":\"Critical\",\"justification\":\"test\"}"
curl.exe -i -X POST "http://localhost:5099/api/tickets/1/priority" -H "X-User-Id: lead-maia"  -H "Content-Type: application/json" -d "{\"priority\":\"Critical\",\"justification\":\"\"}"
```
**Expected:** **403** and **400**.

*Priority decides the target times the ticket is measured against, so an unexplained change is a way
of improving the figures rather than the service.*

---

### TC-Q07 — A restricted ticket is absent, not hidden
**Covers:** AC-9
**Priority:** High.
**Steps:** As Maia, find a **Restricted** ticket assigned to Simone. As Nikau, search the queue for
its reference, then request it directly.

```powershell
curl.exe -i -H "X-User-Id: tech-nikau" "http://localhost:5099/api/tickets/<id>"
```
**Expected:** absent from Nikau's queue, and **404** — not 403. A 403 would confirm the ticket
exists, which for an HR or payroll request is itself a disclosure.

---

### TC-Q08 — The words match the reader
**Covers:** AC-3, AC-6
**Steps:** As the requester who raised a resolved ticket, open it.
**Expected:** The two moves are **Confirm the fix** and **Reopen**, not "Close" and "Start work".
Every SLA state carries a word and a glyph — *Breached*, *At risk*, *Met* — and an overdue ticket says
"overdue by 2h" rather than showing a negative number.

---

## Result record

| Test case | Date | Tester | Result | Evidence | Notes |
|---|---|---|---|---|---|
| TC-Q01 | | | | | |
| TC-Q02 | | | | | |
| TC-Q03 | | | | | |
| TC-Q04 | | | | | |
| TC-Q05 | | | | | |
| TC-Q06 | | | | | |
| TC-Q07 | | | | | |
| TC-Q08 | | | | | |

Screenshot TC-Q04 (the two different button sets), TC-Q06 (the 403 and the 400) and TC-Q07 (the 404).
