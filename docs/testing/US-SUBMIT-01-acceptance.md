# US-SUBMIT-01 — Raise a request and follow it

> **As a staff member, I want to raise a support request and see what is happening to it, so that I
> do not have to email a shared mailbox and hope.**

## Scope note

The problem definition opens with a shared mailbox that nobody owned: requests were lost, duplicated,
and answered twice, and the person who asked had no way of knowing which. This story is the
replacement for that mailbox, and it is the first time the **Requester** role has had a working
screen at all.

Two things distinguish it from a form that merely writes a row. The requester can see the state of
their own request without asking anybody; and what they can see is limited to their own requests **by
the server**, not by this screen choosing to ask a narrow question.

## Acceptance criteria

| ID | Criterion |
|---|---|
| AC-1 | A requester can raise a request with a summary, a category, an urgency and a description. |
| AC-2 | Categories come from a fixed catalogue, so demand can be grouped for analysis. |
| AC-3 | An incomplete request cannot be submitted, and the server validates it again independently. |
| AC-4 | A request can be marked confidential at the point of raising it. |
| AC-5 | The requester is given the reference and can open the request immediately. |
| AC-6 | A requester sees their own requests and nobody else's, enforced server-side. |
| AC-7 | Requests the desk has resolved are distinguished from requests still being worked on. |
| AC-8 | P1 Critical cannot be self-selected; the desk assigns it during triage. |

## Preconditions

API on `http://localhost:5099`, client on `http://localhost:5173`. Sign in as any of the twenty
corporate staff — **Ben Whitfield** works for the steps below. Generating the demonstration dataset
is optional here; an account with no requests is itself a case worth seeing.

---

## Automated tests

```bash
cd client
npm run test
```

`MyRequestsView.test.tsx` covers this story. The API suite covers creation, validation and the
per-requester visibility rule.

---

## Manual test cases

### TC-S01 — The form asks for what triage actually needs
**Covers:** AC-1, AC-2, AC-8
**Steps:** Sign in as Ben Whitfield and press **Raise a request**.
**Expected:** Fields for the summary, category, urgency and description. Category is a dropdown of
nine fixed options, not a text box. Urgency offers **High, Standard, Low** — and a note explains that
P1 Critical is set by the desk, with a fifteen-minute round-the-clock target, rather than requested.

*Free-text categories would give "printer", "Printer" and "printing issue" as three separate
categories, and the demand analysis that justifies the whole system would be worth nothing.*

---

### TC-S02 — The catalogue survives an empty system
**Covers:** AC-2
**Priority:** High — this is a defect that only appears on a first run.
**Steps:** With **no tickets** in the system (clear the demonstration data first), open the form.
**Expected:** All nine categories are offered.

```powershell
curl.exe -s "http://localhost:5099/api/reference/categories"
```
**Expected:** nine categories, regardless of how many tickets exist.

*The endpoint originally returned the distinct categories of existing tickets. That worked while the
database was seeded at start-up and broke the moment it was not: the first requester on a new system
would have been offered nothing to choose from.*

---

### TC-S03 — An incomplete request cannot be sent
**Covers:** AC-3
**Steps:** Type a two-character summary. Leave the category unset. Watch the submit button.
**Expected:** **Raise this request** stays disabled until the summary is at least five characters, a
category is chosen and a description is written.

The disabled button is a convenience. Confirm the server checks independently:

```powershell
curl.exe -i -X POST "http://localhost:5099/api/tickets" -H "X-User-Id: user-02" -H "Content-Type: application/json" -d "{\"title\":\"abc\",\"description\":\"\",\"category\":\"\",\"priority\":\"Standard\"}"
```
**Expected:** **400**, listing *every* problem rather than only the first, so a caller is not made to
resubmit repeatedly to discover them one at a time.

---

### TC-S04 — Raising a request gives back a reference
**Covers:** AC-5
**Steps:** Complete the form and submit.
**Expected:** A confirmation naming the new reference (TKT-000nnn), an **Open it** link, and the
request appearing at the top of the list. The tile row updates.

---

### TC-S05 — A requester sees only their own requests
**Covers:** AC-6
**Priority:** High.
**Steps:** Note the references Ben Whitfield can see. Sign out, sign in as **Aroha Ngata**, and note
hers.
**Expected:** Two disjoint lists. Neither shows the other's requests.

The list being narrow is not the control. Confirm the server enforces it:

```powershell
curl.exe -s -H "X-User-Id: user-02" "http://localhost:5099/api/tickets?assignedTo=tech-nikau"
```
**Expected:** still only Ben Whitfield's own requests. A filter is a convenience applied on top of
what the caller may see, never a way around it.

Then take a reference belonging to somebody else and ask for it directly:

```powershell
curl.exe -i -H "X-User-Id: user-02" "http://localhost:5099/api/tickets/1"
```
**Expected:** **404** if it is not his — the refusal must not confirm that the ticket exists.

---

### TC-S06 — Confidential requests are marked at source
**Covers:** AC-4
**Steps:** Raise a request with **This request is confidential** ticked. Open it.
**Expected:** A **Restricted** marker on the ticket and in the list. Sign in as a technician who is
not assigned to it and confirm it is absent from their queue, and that asking for it by id returns
404 rather than 403.

---

### TC-S07 — Resolved work is separated from live work
**Covers:** AC-7
**Steps:** Look at the three tiles above the list.
**Expected:** **Still open** counts what the desk is working on; **Awaiting your confirmation** counts
what has been resolved and needs the requester to confirm or reopen, and is marked *Needs you* when
it is not zero; **Raised in total** counts everything.

---

## Result record

| Test case | Date | Tester | Result | Evidence | Notes |
|---|---|---|---|---|---|
| TC-S01 | | | | | |
| TC-S02 | | | | | |
| TC-S03 | | | | | |
| TC-S04 | | | | | |
| TC-S05 | | | | | |
| TC-S06 | | | | | |
| TC-S07 | | | | | |

Screenshot TC-S01 (the form), TC-S04 (the reference) and TC-S05 (the two disjoint lists plus the
curl 404). Those three are what show the mailbox has actually been replaced rather than re-skinned.
