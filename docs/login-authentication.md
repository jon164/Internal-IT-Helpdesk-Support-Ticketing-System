# Sign-in and authentication

Engineering notes for the change that replaced the demo identity picker with a
real username-and-password sign-in. These are repository notes for the team,
not report text.

## What changed

| Before | After |
|---|---|
| Sign-in page was a dropdown of three roles. No credential was checked. | Username and password, checked by the API. |
| The browser sent `X-User-Id: it-manager` and the API believed it, so anyone could act as any account by sending that header. | The API issues a random session token at sign-in. Requests carry `Authorization: Bearer <token>`. `X-User-Id` is no longer read. |
| The role was kept in `localStorage` and could be switched from the top bar. | The role comes from the account record on the server. The role switcher is gone; to change role, sign out and sign in as another account. |
| The API client logged every request body to the console. | Request bodies are not logged — the sign-in body contains a password. |

## How it works

**Server** (`src/Helpdesk.Api/Auth/`, `src/Helpdesk.Core/Security/`)

- `POST /api/auth/login` `{ username, password }` returns `{ token, expiresAtUtc, user }`.
  - `400` blank username or password · `401` any wrong credential · `429` account locked (with `Retry-After`).
  - Responses send `Cache-Control: no-store`.
- `GET /api/auth/me` returns the signed-in account, or `401`.
- `POST /api/auth/logout` revokes the session. Always `204`, so the client can always clear its state.
- **Passwords** are stored as salted PBKDF2-HMAC-SHA256 with 600,000 iterations (`PasswordHasher`), compared in constant time. The plain text is never stored.
- **Sessions** live in a `UserSessions` table. The client holds a 256-bit random token; the table holds only its SHA-256 hash, so a copy of the database does not give anyone a working session. Sessions last 8 hours (one shift), end on sign-out, and end immediately if the account is deactivated.
- **No account enumeration.** An unknown username, a deactivated account, an account with no password and a wrong password all get the same `401` and message. Unknown usernames are still put through a full password hash, so the response time does not give the difference away (measured in a Linux build container: 143 ms for a real check, 141 ms for the dummy).
- **Lockout.** Five consecutive wrong passwords lock the account for 15 minutes. The correct password does not unlock it early. A successful sign-in resets the count. Settings are in `appsettings.json` under `Authentication`.
- **Demo passwords** are read from configuration (`DemoAccounts:Passwords` in `appsettings.Development.json`), not source code. In any other environment none are configured, so no account can sign in. The seeder never overwrites a password that is already set.

**Client** (`client/src/components/LoginForm.tsx`, `client/src/services/auth.ts`, `client/src/App.tsx`)

- Labelled fields with `autocomplete="username"` / `current-password`, so password managers work.
- Inline validation linked to each field with `aria-describedby`; focus moves to the first problem.
- Errors are announced through `role="alert"`; "signed out" and "session ended" through `role="status"`.
- Show/hide password, Caps Lock warning, a disabled "Signing in…" button that blocks double submission, and the password field cleared after a failed attempt.
- Distinct messages for wrong credentials, lockout, server error and server unreachable.
- The token is kept in `sessionStorage`, so a refresh keeps you signed in and closing the tab signs you out. On load the app asks `/api/auth/me` whether the stored token is still valid.
- Any `401` mid-session returns you to the sign-in page with "Your session has ended."

## Tests

Quality areas covered: **security** (primary), **usability**, **accessibility** and **reliability**.

### Execution status (11 Oct 2026)

- **Client: 26 / 26 passing** (`npm test`, on a team Windows machine and in the build container), plus `npm run build` and `npm run lint` clean (0 errors; the 3 existing warnings are in files this change did not touch).
- **Backend: 78 / 78 passing** (`dotnet test`, .NET SDK 10, Windows, 11 Oct 2026): the 31 sign-in tests above (54 runs, counting each data row) plus the existing 24 ticket tests. Build had only warnings: NU1903 on `SQLitePCLRaw.lib.e_sqlite3` (pre-existing, no patched version) and four MSTest style suggestions in the new tests.

### Where they live and how to run them

| Suite | Location | Command | Runs in CI |
|---|---|---|---|
| Backend unit tests (MSTest) | `tests/Helpdesk.Tests/Auth/` | `dotnet test` | Yes — `Backend build, tests, and smoke check` |
| Client component and journey tests (Vitest, React Testing Library, axe-core) | `client/src/__tests__/` | `cd client && npm test` | Yes — `Client build, lint and tests` |
| HTTP security smoke checks against the running API | `.github/workflows/quality-checks.yml` | CI only | Yes |

### Test register

| ID | What it checks | Quality area | Technique |
|---|---|---|---|
| AUTH-B01–B06 | Hashing: verifies the right password, rejects near-misses, salted, no plain text, fails closed on corrupt stored values | Security | Equivalence partitioning (exact / near-miss / blank / malformed) |
| AUTH-B07–B08 | Successful sign-in stores only the token's hash; usernames trimmed and case-insensitive | Security, usability | Equivalence partitioning |
| AUTH-B09 | Five kinds of failure all return the same result and no account details | Security | Equivalence partitioning |
| AUTH-B10 | Blank input rejected and not counted towards lockout | Security, usability | Equivalence partitioning |
| AUTH-B11–B14 | Lockout at 5 failures; correct password does not bypass it; lifts exactly at its end; resets on success; per account | Security, reliability | Boundary-value analysis (4 / 5 / 6 attempts; 1 s before / at lockout end) |
| AUTH-B15–B18 | Session validation: live token accepted; expiry at 1 s before / at / 1 s after; blank, unknown and oversized tokens refused | Security, reliability | Boundary-value analysis, equivalence partitioning |
| AUTH-B19–B22 | Sign-out revokes only that session and is idempotent; deactivating an account ends its sessions; dead sessions are cleaned up | Security, reliability | State-based (session lifecycle) |
| AUTH-B23 | Tokens are long and unique | Security | Sampling |
| AUTH-B24 | **Regression:** naming an account in `X-User-Id` no longer authenticates | Security | Regression test for the replaced design |
| AUTH-B25–B28 | Middleware resolves the role from the database; every malformed header leaves the caller anonymous | Security | Equivalence partitioning |
| AUTH-B29–B31 | No passwords without configuration; configured ones are hashed; re-seeding does not reset them | Security | Equivalence partitioning |
| AUTH-C01–C04 | Accessible names, autocomplete hints, errors linked to fields, failures announced, **automated WCAG 2.1 A/AA scan with axe-core** before and after an error | Accessibility | Automated accessibility rules + assertions on the accessibility tree |
| AUTH-C05–C12 | Whitespace-only username, Enter submits, trimming, pending state blocks double submit, password cleared and refocused, lockout / offline messages, show/hide, status vs alert | Usability | Boundary value (whitespace), scenario-based |
| AUTH-C13–C17 | Password never reaches the console; Bearer token sent, `X-User-Id` never sent; no header when signed out; 401 mid-session vs at sign-in; unknown roles get least privilege | Security | Equivalence partitioning, regression |
| AUTH-C18–C20 | Token in `sessionStorage` not `localStorage`; still works if storage throws; distinct messages per failure class | Reliability, usability | Fault injection (storage throws) |
| AUTH-C21–C26 | Full journeys through `App`: sign in, no role switcher, restore after refresh, expired stored session, sign-out on server, sign-out when server is down | Reliability, security | Scenario / journey tests with a fake API |
| CI smoke | Against the real running API: `/me` without token → 401; `X-User-Id` → 401; forged token → 401; blank login → 400; dev password outside Development → 401; `Cache-Control: no-store` | Security | System-level black-box checks |

### Why these methods

- **Security is the main risk** because the previous design let any caller act as the IT Manager by sending one header. The highest-value test is therefore the regression test that sends that header and expects to be refused (AUTH-B24 at unit level, and again against the real API in CI).
- **The rules sit in `AuthService`, not in the endpoints**, so they can be unit-tested with EF Core InMemory and a controllable clock. That makes the time-based rules (expiry, lockout end) testable exactly on their boundaries in milliseconds instead of waiting 15 minutes or 8 hours.
- **Boundary-value analysis** is used on the three numeric limits (failed attempts, lockout end, session expiry), because off-by-one mistakes (`<` vs `<=`) are the most likely defect there and the most damaging: one extra guess, or a token that lives one tick too long.
- **Equivalence partitioning** is used on credentials because there are endless possible inputs but only a few behaviours: right, near-miss, blank, unknown account, deactivated account, malformed stored data.
- **The middleware test uses a real `DefaultHttpContext`** rather than mocks, so it exercises the actual header parsing without needing a web host or an extra NuGet package.
- **axe-core** catches the WCAG failures a person can miss (missing labels, broken ARIA references) on every run. It cannot judge colour contrast in jsdom, so contrast still needs a manual check in a real browser.
- **The CI smoke checks** test the deployed configuration rather than the code: in particular that the Development demo passwords do not work anywhere else.

## Known limitations

- **Most ticket endpoints still do not check who is calling.** Only the dashboard and demo-data endpoints enforce a role. Sign-in now establishes *who* the caller is reliably, but `/api/tickets` and its sub-routes are still reachable without a token. This is the next security change, and the earlier Task 3 results already record two failures that come from it.
- The token is readable by JavaScript (`sessionStorage`), so a cross-site scripting flaw elsewhere could steal it. An `HttpOnly` cookie would avoid that, at the cost of CSRF protection.
- Lockout can be used to lock out a known user on purpose (denial of service). Rate-limiting by IP address would reduce that.
- There is no password change, password reset, or account creation.
- Demo passwords sit in `appsettings.Development.json`, in the repository. Acceptable for a prototype; a real deployment would use user secrets or a secret store.
- Upgrading deletes an older `helpdesk.db` because there are no EF migrations yet.
