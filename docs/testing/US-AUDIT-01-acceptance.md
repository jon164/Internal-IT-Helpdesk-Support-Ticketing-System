# US-AUDIT-01 — Complete audit trail

> **As anybody who can see a ticket, I want the complete record of what has happened to it, so that
> nobody has to reconstruct events from memory or from a mailbox.**

## Scope note

This is the capability the problem definition is really asking for. The shared mailbox could not say
who had picked a request up, when it was first answered, why it stalled for a fortnight, or who
decided it was urgent. Every one of those is a question about history, and none of them survives in a
system that stores only current state.

Two design decisions are worth stating in the report:

- **The trail is shown to every role that can see the ticket**, not reserved for management. A
  requester being able to see who has their request is the point of replacing the mailbox.
- **Nothing edits or deletes an entry.** There is no route that does, in the API or the interface.
  An audit trail that can be corrected is a log, not a record.

## Acceptance criteria

| ID | Criterion |
|---|---|
| AC-1 | Every state-changing operation writes an entry: creation, transition, assignment, priority change, first response, hold start and end, comment. |
| AC-2 | Each entry names what happened, when, who did it and in what role. |
| AC-3 | Entries are ordered and shown in full, with no pagination that could hide one. |
| AC-4 | The trail is visible to the requester, the assigned technician and the service manager alike. |
| AC-5 | No interface control, and no API route, edits or removes an entry. |
| AC-6 | Where an entry carries a reason or note, that text is shown — it is the reason the entry exists. |
| AC-7 | Hold periods are recorded with their reason and are visibly excluded from the resolution clock. |

## Preconditions

API on `http://localhost:5099`, client on `http://localhost:5173`, demonstration dataset generated.

---

## Automated tests

```bash
cd client
npm run test
```

`TicketDetailPage.test.tsx` covers the trail's rendering. Server-side, the domain and API suites
assert that each operation writes its event and that no seeded ticket exists without one.

---

## Manual test cases

### TC-T01 — A whole life, end to end
**Covers:** AC-1, AC-2, AC-3, AC-6
**Priority:** High — this single walkthrough is the best evidence for this story.
**Steps:** Carry one ticket through its whole life and read the history afterwards.

1. As **Ben Whitfield**, raise a request.
2. As **Simone Delacroix**, open it from **Unassigned**: Triage → Assign to me → Start work.
3. Place it on hold with a reason, then resume it.
4. Post a comment.
5. Resolve it with notes.
6. As Ben, open it and read the history.

**Expected:** Entries in order for *Raised*, each *Status changed* with its transition rendered
readably (`New → Triaged`), *Assigned* naming who, *First response* recorded automatically when work
started, *Placed on hold* with its reason, *Taken off hold*, *Comment*, and the resolution with its
notes. Each entry names the person and their role.

*Note that the transition is shown once, readably, rather than twice. The stored detail is
`"InProgress -> Resolved. <notes>"`; the header renders the transition and the view strips that
prefix so the raw enum names do not appear beside the readable ones — while keeping the notes, which
are what the reader came for.*

---

### TC-T02 — First response is recorded, not claimed
**Covers:** AC-1
**Steps:** On a fresh ticket, note that **First response** reads *Not yet answered*. Post a comment as
a technician, or start work.
**Expected:** A *First response* entry appears with the time, the response clock settles to **Met** or
**Missed**, and the field on the ticket fills in. The interface warns before the comment is posted
that it will stop the clock.

*Response attainment on the manager's dashboard is computed from this timestamp. If it could be set
by hand the figure would measure honesty rather than service.*

---

### TC-T03 — The trail is not management-only
**Covers:** AC-4
**Steps:** Open the same ticket as the requester, the assigned technician, and the service manager.
**Expected:** All three see the identical history. What differs between them is only the **What you
can do** panel.

---

### TC-T04 — Nothing edits the record
**Covers:** AC-5
**Steps:** Look for any edit or delete control on an entry. Then look for a route.

```powershell
curl.exe -i -X DELETE "http://localhost:5099/api/tickets/1/events/1" -H "X-User-Id: lead-maia"
curl.exe -i -X PUT    "http://localhost:5099/api/tickets/1/events/1" -H "X-User-Id: lead-maia"
curl.exe -s "http://localhost:5099/openapi/v1.json" | Select-String "events"
```
**Expected:** no control in the interface; **404** for both calls, because no such route is
registered; and nothing in the API document that writes to an event.

*This is an absence, which is harder to demonstrate than a presence — which is why it is worth
recording the OpenAPI document as evidence rather than only the two 404s.*

---

### TC-T05 — Holds are recorded and excluded from the clock
**Covers:** AC-7
**Steps:** On a P3 ticket, note the resolution time remaining. Place it on hold with a reason. Wait a
few minutes. Resume it and note the remaining time again.
**Expected:** The remaining time has not moved by the time spent on hold. The ticket lists the hold
window with its reason under **Time on hold is excluded from the resolution clock**, and both the
start and the end appear in the history.

*Without this, a ticket waiting a week on a part the supplier had not shipped would breach a target
the team had no way of meeting, and the attainment figure would measure the supplier.*

---

### TC-T06 — Account changes have their own trail
**Covers:** AC-5
**Steps:** See `US-ADMIN-01-acceptance.md`, TC-A04 and TC-A07.
**Expected:** Role changes and deactivations are recorded separately, with the same guarantee: reason
required, nothing edits an entry.

*Two trails rather than one, because they answer different questions and have different readers. A
technician needs the ticket's history; only the service manager needs the account history.*

---

## Result record

| Test case | Date | Tester | Result | Evidence | Notes |
|---|---|---|---|---|---|
| TC-T01 | | | | | |
| TC-T02 | | | | | |
| TC-T03 | | | | | |
| TC-T04 | | | | | |
| TC-T05 | | | | | |
| TC-T06 | | | | | |

Screenshot the full history from TC-T01 — one image of a complete ticket life is worth more to a
marker than any description of the mechanism.
