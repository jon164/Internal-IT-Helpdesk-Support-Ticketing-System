# US-ADMIN-01 — Sign-in and account administration

> **As the IT support team lead, I want to sign in and manage who holds which role, so that access is
> granted deliberately and revoked reliably.**

## Scope note

Read `docs/scope-change-authentication.md` first. In short: the sign-in screen selects an identity
and verifies no credential. Authorisation is real and enforced server-side; authentication is not
implemented and is declared out of scope. Every criterion below tests authorisation, not
authentication.

Account administration belongs to the **service manager** (Maia Thornton). There is no separate
administrator account — a four-person desk would not employ one.

## Acceptance criteria

| ID | Criterion |
|---|---|
| AC-1 | A user signs in by choosing an account, and the screen states plainly that no password is checked. |
| AC-2 | An unknown or deactivated account cannot sign in, and the two cases are indistinguishable. |
| AC-3 | A signed-in session survives a page refresh and is re-validated against the server. |
| AC-4 | The service manager can see all accounts and change a role, giving a reason. |
| AC-5 | Accounts can be deactivated and reactivated; deactivation takes effect immediately. |
| AC-6 | The service manager cannot change their own role or deactivate their own account. |
| AC-7 | The last active service manager cannot be removed. |
| AC-8 | Nobody except the service manager can reach account administration. |
| AC-9 | Every account change is recorded with who, what, when and why, and the record cannot be edited. |
| AC-10 | The application starts with no tickets, and the service manager can generate or clear the demonstration dataset on demand. |
| AC-11 | The routes that generate and clear data do not exist in a production build, and are refused for anyone but the service manager in a development one. |

## Demo accounts

| Account | Role |
|---|---|
| Maia Thornton | Service manager — also administers accounts |
| Nikau Ashford, Simone Delacroix, Raj Bhandari | Technicians |
| Twenty corporate staff | Requesters |

No passwords — see the scope note.

**There are no tickets on a first start.** Accounts are created so somebody can sign in; the ticket
table is left empty. Generate a dataset from **Accounts → Demonstration data**.

> **If you previously ran a build that had a separate "Devon Ashworth" administrator account**, start
> the API once more. It removes that account and its audit entries on start-up, and logs that it has
> done so. Nothing else in your database is touched.

---

## Automated tests

```bash
cd client
npm run test
```

92 tests across nine files. `LoginPage.test.tsx`, `AdminView.test.tsx`,
`SampleDataPanel.test.tsx` and `DashboardPage.test.tsx` cover this story.

Server-side, the API suite covers sign-in, every guard, and the audit trail.

---

## Manual test cases

### TC-A01 — Sign-in is honest about what it does
**Covers:** AC-1
**Steps:** Open the app signed out.
**Expected:** A sign-in card with an account picker grouped by role, **no password field**, and a
visible notice that no password is required and that the screen selects an identity rather than
verifying a credential.

*A password box that accepted anything would be worse than none: it invites the reader to believe a
check happened.*

---

### TC-A02 — Session survives a refresh, and is re-checked
**Covers:** AC-3
**Steps:** Sign in as Maia Thornton. Refresh the page. Then sign out and refresh again.
**Expected:** The first refresh returns you to the dashboard without signing in again; after signing
out, a refresh leaves you on the sign-in screen.

---

### TC-A03 — Accounts is offered only to the service manager
**Covers:** AC-8
**Steps:** Sign in as Maia Thornton and note the tabs. Sign out, sign in as Nikau Ashford, then as a
requester, and note them again.
**Expected:** Maia sees **Performance, Backlog, Queue, Accounts, My requests**. Nikau sees **Queue**
and **My requests**. A requester sees only **My requests**, and therefore no tab bar at all.

Each role lands on a screen that is theirs: a technician on work, a requester on their own requests, a
manager on the report. Offering a technician a reporting tab that always refuses is a broken link,
not a security demonstration.

The tabs are a convenience, not the control — every request behind them is authorised server-side, and
the reporting view still renders the server's refusal verbatim if it is reached by any other means
(`DashboardPage.test.tsx` covers both). Confirm the control itself:

```powershell
curl.exe -i -H "X-User-Id: tech-nikau" "http://localhost:5099/api/admin/users"
curl.exe -i -H "X-User-Id: user-01"    "http://localhost:5099/api/admin/users"
curl.exe -i                            "http://localhost:5099/api/admin/users"
curl.exe -i -H "X-User-Id: tech-nikau" "http://localhost:5099/api/admin/audit"
curl.exe -i -H "X-User-Id: tech-nikau" "http://localhost:5099/api/reports/summary"
```

**Expected:** 403, 403, 401, 403, 403.

---

### TC-A04 — Role changes require a reason
**Covers:** AC-4, AC-9
**Steps:** As Maia, change a requester to Technician. Try to apply with the reason blank, then supply
one.
**Expected:** "Apply change" stays disabled until a reason is typed. Afterwards the account change
history shows the change, both roles, Maia's name, and the reason.

---

### TC-A05 — Deactivation takes effect immediately
**Covers:** AC-5, AC-2
**Steps:** Deactivate an account with a reason. Sign out. Try to sign in as that account.
**Expected:** The account is marked Inactive but remains listed — it is still part of the record.
Sign-in is refused. Reactivating restores access, and both events appear in the history.

```powershell
curl.exe -i -H "X-User-Id: user-05" "http://localhost:5099/api/tickets"
```
Expected: **401** while deactivated, even though the account exists.

Also confirm the refusal wording is identical for a deactivated account and a made-up one —
distinguishing them would tell an unauthenticated caller which accounts exist:

```powershell
curl.exe -s -X POST "http://localhost:5099/api/session" -H "Content-Type: application/json" -d "{\"userId\":\"user-05\"}"
curl.exe -s -X POST "http://localhost:5099/api/session" -H "Content-Type: application/json" -d "{\"userId\":\"nobody\"}"
```

---

### TC-A06 — The service manager cannot lock themselves out
**Covers:** AC-6, AC-7
**Priority:** High — this is the guard that prevents an unrecoverable state.
**Steps:** As Maia, try to change your own role, and to deactivate yourself.
**Expected:** Both controls are disabled on your own row, marked "you". Confirm the server refuses it
too, so the guard is not merely a disabled button:

```powershell
curl.exe -i -X POST "http://localhost:5099/api/admin/users/lead-maia/role" -H "X-User-Id: lead-maia" -H "Content-Type: application/json" -d "{\"role\":\"Requester\",\"reason\":\"test\"}"
```
Expected: **409 Conflict**.

Then promote a technician to Service manager and have *that* account demote Maia. It should succeed —
the guard is about the last service manager, not about service managers generally. Demote the second
account again and confirm the "Single point of failure" warning returns.

---

### TC-A07 — The record explains itself
**Covers:** AC-9
**Steps:** Read the account change history after TC-A04 and TC-A05.
**Expected:** Each entry names the event, the account affected, the manager responsible, the time, and
the reason. Nothing in the interface edits or deletes an entry.

---

### TC-A08 — The application starts empty, and fills only when asked
**Covers:** AC-10
**Steps:** Stop the API, delete `src/Helpdesk.Api/helpdesk.db`, and start it again. Sign in as Maia
and look at Performance, then at Accounts.
**Expected:** The API logs that the ticket table is empty. Performance shows *"No tickets to report
on"* and draws no tiles or charts — a wall of zeroes with a green tick beside "0 breached" would
read as an achievement rather than an absence. Accounts shows *Demonstration data* with **No
tickets** and an enabled **Generate 500 tickets** button.

Press it. Performance and Backlog now report on 500 tickets. Press **Clear all tickets**, confirm,
and both views return to the empty notice while every account and every account-change entry remains.

*Why this matters for the report: the earlier build seeded 500 tickets automatically at first start,
which meant a demonstration opened onto data with no visible origin. A reader could not tell what
was fabricated. Now the fabrication is an explicit, observable act.*

---

### TC-A09 — Data-fabricating routes are absent from a production build
**Covers:** AC-11
**Priority:** High — this is the control, and it is stronger than a hidden button.
**Steps:** With the API running normally (Development), confirm the routes answer. Then run it as
Production and confirm they are gone while the rest of the API still works.

```powershell
# Development
curl.exe -i -H "X-User-Id: lead-maia"  "http://localhost:5099/api/admin/sample-data"
curl.exe -i -H "X-User-Id: tech-nikau" "http://localhost:5099/api/admin/sample-data"
curl.exe -i -X POST -H "X-User-Id: tech-nikau" "http://localhost:5099/api/admin/sample-data"
curl.exe -i -X DELETE -H "X-User-Id: user-01"  "http://localhost:5099/api/admin/sample-data"
```

**Expected:** 200, 403, 403, 403. A technician and a requester are refused: "only developers run this
build" is an assumption about deployment, not an access control.

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Production"
dotnet run --project src/Helpdesk.Api
```

```powershell
curl.exe -i -H "X-User-Id: lead-maia" "http://localhost:5099/api/admin/users"
curl.exe -i -H "X-User-Id: lead-maia" "http://localhost:5099/api/admin/sample-data"
```

**Expected:** **200** and **404**. Account administration still works; the sample-data route does not
exist. The panel disappears from the interface too — it treats the 404 as the correct answer rather
than an error.

Remember to clear the variable afterwards: `Remove-Item Env:ASPNETCORE_ENVIRONMENT`.

*A 404 here is a stronger claim than a 403. A route that is never registered cannot be reached by a
misconfigured role, a forged header, or a future change to the authorisation rules.*

---

## Result record

| Test case | Date | Tester | Result | Evidence | Notes |
|---|---|---|---|---|---|
| TC-A01 | | | | | |
| TC-A02 | | | | | |
| TC-A03 | | | | | |
| TC-A04 | | | | | |
| TC-A05 | | | | | |
| TC-A06 | | | | | |
| TC-A07 | | | | | |
| TC-A08 | | | | | |
| TC-A09 | | | | | |

Screenshot TC-A01 (the honesty notice), TC-A03 (the refusal for a technician), TC-A06 (the lockout
guard) and TC-A09 (the 404 in a production build). Those four are what turn "we added a login" into a
security argument.
