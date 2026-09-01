# Scope change — sign-in and account administration

**Raised:** 1 September 2026
**Affects:** Task 1 §1.7 (scope boundaries), and the roles table in §1.3

This note records a deliberate change to the agreed scope, so the report and the prototype do not
contradict each other. Fold the wording below into Task 1 rather than leaving the original text in
place — a marker who compares the two will otherwise find the prototype doing something the problem
definition says is out of scope.

---

## What changed

Two additions:

1. **A sign-in screen.** The prototype previously selected an identity from a dropdown in the header.
   It now has a sign-in screen and a signed-in session, with sign-out.
2. **Account administration.** The service manager can change somebody's role, and activate or
   deactivate an account. Every such change requires a reason and is recorded permanently.

**The role count is unchanged.** Account administration belongs to the existing service manager
rather than to a new administrator role, so §1.6's feasibility argument — three user roles, one core
workflow — still holds exactly as written.

## What did *not* change

**No authentication was added.** The sign-in screen establishes *which account the caller is acting
as*. It verifies no credential; there is no password field, because a password box that accepted
anything would invite a reader to believe a check had happened. The screen says so itself, so nobody
watching a demonstration is misled.

What the sign-in screen does establish is real, and worth being precise about, because it is the
difference between a meaningful control and a cosmetic one:

- The client sends an account identifier. **It never sends a role.**
- The server reads the role from that account's own database record on every single request.
- Every authorisation decision is made server-side against that record.

A caller therefore cannot grant themselves anything by editing what the browser sends. They can only
claim to be a different known account — which is exactly what a prototype identity switcher is for.
Adding real authentication means adding a credential to one endpoint and a check in its handler; not
one authorisation rule would change.

## Suggested replacement wording for Task 1

**§1.3, roles table — extend the service manager row:**

> | IT support team lead (service manager) | Oversee workload distribution, monitor target attainment, report performance, manage escalations, and administer user accounts and role assignments |

**§1.7, out of scope — replace the identity-provider line with:**

> - Authentication. The prototype provides a sign-in screen that establishes which account the caller
>   is acting as, but verifies no credential. Authorisation is enforced server-side against each
>   account's stored role and is fully exercised by the test suite; authentication itself, and any
>   integration with an external identity provider or staff directory, is out of scope.

**§1.7, in scope — add:**

> - Account administration: role assignment, account deactivation, and an audit trail of both

## Why the service manager rather than a separate administrator role

A separate administrator role was considered and rejected. On a four-person support desk there is no
dedicated systems administrator; the team lead does both jobs. Modelling a role the organisation
would not employ would make the prototype less faithful to the problem, not more rigorous.

The trade this makes is worth stating plainly, because it is a real one. Concentrating operational
and account authority in one account means the person who can grant roles can also read every ticket,
including the restricted HR and Finance ones. In a larger organisation that concentration would be
separated. Here it is accepted, with three compensating controls:

- Every account change demands a written reason and is recorded permanently, naming the person who
  made it, the account affected, and the time.
- The service manager cannot change their own role or deactivate their own account.
- The last active service manager cannot be demoted or deactivated, so the system cannot be locked in
  a state only direct database access could repair.

## Quality attributes this strengthens

**Security.** The problem definition opens with a shared mailbox that two departed staff could still
read, because nobody recorded — or noticed — that their access was never revoked. Deactivation now
takes effect on the next request, and the account-change log makes the omission visible. The specific
failure described in §1.2 is now both preventable and detectable.

**Maintainability.** `TicketAccessPolicy.CanView` was rewritten from a chain of early returns ending
in the technician rule to an exhaustive switch on the role. The old form's fall-through meant that
adding a role to the system silently granted it queue access — a security control quietly widening
because nobody remembered to edit it. This was found while briefly prototyping a fourth role, and is
worth a sentence in Task 4: it is a defect that only appears when the code is *extended*, which is
precisely the class of defect a review catches and a passing test suite does not.

## Known limitations to declare in Task 7

- No credential is verified at sign-in.
- The session is a remembered account identifier in browser storage. It carries no role and no
  privilege, and is re-validated against the server on every page load — so an account deactivated
  since the last visit is rejected on the way back in — but it is not a signed or expiring token.
- There is no password policy, lockout, or session expiry, because there is no password.
- Operational and account authority are concentrated in one role, as discussed above.
