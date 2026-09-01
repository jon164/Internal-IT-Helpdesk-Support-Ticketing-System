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

## Demo accounts

| Account | Role |
|---|---|
| Maia Thornton | Service manager — also administers accounts |
| Nikau Ashford, Simone Delacroix, Raj Bhandari | Technicians |
| Twenty corporate staff | Requesters |

No passwords — see the scope note.

> **If you previously ran a build that had a separate "Devon Ashworth" administrator account**, start
> the API once more. It removes that account and its audit entries on start-up, and logs that it has
> done so. Nothing else in your database is touched.

---

## Automated tests

```bash
cd client
npm run test
```

52 tests across five files. `LoginPage.test.tsx` and `AdminView.test.tsx` cover this story.

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
**Steps:** Sign in as Maia Thornton and note the tabs. Sign out, sign in as Nikau Ashford, and note
them again.
**Expected:** Maia sees **Performance, Backlog, Accounts**. Nikau sees **Performance, Backlog** only
— and both of those show the server's refusal rather than an empty page.

The tab being hidden is a convenience, not the control. Confirm the control itself:

```powershell
curl.exe -i -H "X-User-Id: tech-nikau" "http://localhost:5099/api/admin/users"
curl.exe -i -H "X-User-Id: user-01"    "http://localhost:5099/api/admin/users"
curl.exe -i                            "http://localhost:5099/api/admin/users"
curl.exe -i -H "X-User-Id: tech-nikau" "http://localhost:5099/api/admin/audit"
```

**Expected:** 403, 403, 401, 403.

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

Screenshot TC-A01 (the honesty notice), TC-A03 (the refusal for a technician) and TC-A06 (the lockout
guard). Those three are what turn "we added a login" into a security argument.
