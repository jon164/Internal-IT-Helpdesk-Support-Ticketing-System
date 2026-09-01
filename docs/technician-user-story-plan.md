# Technician User Story Delivery Plan

## 1. Objective

Deliver the Technician workstream for the internal IT helpdesk so technicians can efficiently manage assigned work, update ticket status, communicate internally, and close resolved work without leaving the active queue in an inconsistent state.

This plan is based on the following MoSCoW story set and acceptance criteria:

- Must Have: US-2.1, US-2.3, US-2.8
- Should Have: US-2.2, US-2.5
- Could Have: US-2.4, US-2.6, US-2.7

---

## 2. Scope and constraints

### In scope

- Technician dashboard for assigned tickets
- Unassigned ticket claim flow
- Ticket status workflow and validation rules
- Internal notes and visibility rules
- Pending employee response and auto-return workflow
- Ticket closure and timeout-based auto-close
- Ticket linking and reassignment for escalation

### Out of scope

- Manager analytics dashboards
- Employee-facing self-service portal
- Email/SMS notifications integration
- Advanced SLA reporting beyond ticket age and status clocks
- Bulk operations or mass reassignment

### Core constraints

- Ticket status transitions must remain deterministic and auditable.
- Technician and employee visibility must be enforced by role.
- Queue ordering must be stable: priority first, age second.
- Auto-close must be tied to a defined timeout rule and logged in the audit trail.

---

## 3. Delivery approach

This work should be delivered in three phases aligned to the MoSCoW prioritization.

### Phase 1 — Must Have

Build the minimum viable technician workflow that enables day-to-day issue handling:

1. Assignment queue and sorting
2. Ticket status transitions
3. Resolved-to-closed workflow

### Phase 2 — Should Have

Add operational support functions that reduce duplicate work and keep technical notes private:

1. Claiming unassigned tickets
2. Internal notes

### Phase 3 — Could Have

Extend the technician experience with escalation and lifecycle refinements:

1. Reassignment and specialist routing
2. Pending employee response state
3. Related and duplicate ticket linking

---

## 4. Work breakdown

### 4.1 Foundation and data model

#### Objective

Establish the domain model and server-side rules required to support the technician workflow.

#### Tasks

- Confirm or extend the `Ticket` domain model with the required queue and status metadata.
- Validate the `TicketStatus` enum and transition rules in `TicketStatusRules`.
- Ensure each ticket tracks:
  - assignment owner
  - status timestamps
  - pending since
  - resolved/closed timestamps
  - closed reason
  - audit trail entries
- Define how queue ordering is computed from priority and age.
- Confirm internal note visibility rules and note storage model.

#### Deliverables

- Status transition matrix
- Queue order rule
- Audit log structure
- Ticket metadata model for lifecycle states

#### Acceptance coverage

- AT-2.1.1, AT-2.3.1, AT-2.3.2, AT-2.8.2

---

### 4.2 Must Have: Assigned queue and status lifecycle

#### User stories

- US-2.1 — View assigned ticket queue
- US-2.3 — Transition ticket status
- US-2.8 — Close resolved tickets

#### Tasks

1. Implement the assigned-tickets query for a technician.
2. Sort results by:
   - priority descending (highest first)
   - age descending (oldest first)
3. Show an empty-state message when no tickets are assigned.
4. Add status transition endpoints for New → In Progress → Pending Employee Response → Resolved → Closed.
5. Reject invalid transitions with a clear list of valid next states.
6. Add closure validation so only resolved tickets can close.
7. Record closure reason and auto-close reason in audit history.
8. Remove closed tickets from the active queue after closure.

#### API / UI work

- Backend endpoints:
  - `GET /api/technicians/{technicianId}/tickets`
  - `POST /api/tickets/{ticketId}/status`
  - `POST /api/tickets/{ticketId}/close`
- Frontend:
  - Technician queue screen
  - Ticket detail panel
  - Status action controls
  - Empty state message

#### Acceptance coverage

- AT-2.1.1, AT-2.1.2
- AT-2.3.1, AT-2.3.2
- AT-2.8.1, AT-2.8.2

---

### 4.3 Should Have: Claiming and internal notes

#### User stories

- US-2.2 — Claim an unassigned ticket
- US-2.5 — Add internal notes

#### Tasks

1. Add unassigned ticket listing.
2. Implement claim action with ownership transfer validation.
3. Prevent duplicate claims if a ticket is already assigned.
4. Return ownership details when a claim conflict occurs.
5. Add internal note storage and role-based visibility checks.
6. Validate blank notes and show input errors to the technician.
7. Expose internal notes only to IT staff and managers.

#### API / UI work

- Backend endpoints:
  - `GET /api/tickets/unassigned`
  - `POST /api/tickets/{ticketId}/claim`
  - `POST /api/tickets/{ticketId}/notes`
- Frontend:
  - Unassigned pool list
  - Claim action
  - Internal note form
  - Validation messaging

#### Acceptance coverage

- AT-2.2.1, AT-2.2.2
- AT-2.5.1, AT-2.5.2

---

### 4.4 Could Have: Escalation, employee wait states, and duplicate tracking

#### User stories

- US-2.4 — Reassign a ticket
- US-2.6 — Request more info from employee
- US-2.7 — Link related/duplicate tickets

#### Tasks

1. Allow reassignment only to active, available technicians.
2. Show blocked technician options and available list on invalid reassignment.
3. Introduce `Pending Employee Response` status and pause backlog clock behavior.
4. Auto-return to `In Progress` when the employee adds a comment.
5. Add duplicate/related ticket links and self-link prevention.
6. Display visible links on both tickets.

#### API / UI work

- Backend endpoints:
  - `POST /api/tickets/{ticketId}/reassign`
  - `POST /api/tickets/{ticketId}/request-info`
  - `POST /api/tickets/{ticketId}/links`
- Frontend:
  - reassignment modal or dropdown
  - request-info action
  - related ticket linking UI

#### Acceptance coverage

- AT-2.4.1, AT-2.4.2
- AT-2.6.1, AT-2.6.2
- AT-2.7.1, AT-2.7.2

---

## 5. Acceptance test plan

### Functional test matrix

| Story | Key checks | Priority |
|---|---|---|
| US-2.1 | Queue ordering, empty state | Must Have |
| US-2.2 | Claim from unassigned pool, conflict handling | Should Have |
| US-2.3 | Valid transitions, invalid rejection, audit timestamp | Must Have |
| US-2.4 | Reassign to active technician, invalid selection blocked | Could Have |
| US-2.5 | Internal note save, blank note validation, visibility rules | Should Have |
| US-2.6 | Pending employee response pause and resume | Could Have |
| US-2.7 | Duplicate link creation and self-link rejection | Could Have |
| US-2.8 | Resolved ticket close and timeout auto-close | Must Have |

### Test types

- Unit tests for transition rules and domain validation
- Service tests for claim, reassignment, note creation, and closure behavior
- Integration tests for API endpoints and response payloads
- UI tests for queue rendering, claim flow, and form validation

---

## 6. Release sequence

### Release 1 — Technician essentials

- Assigned queue
- Status transition workflow
- Close resolved tickets
- Auto-close timeout logic

### Release 2 — Operational support

- Claim unassigned tickets
- Internal notes

### Release 3 — Lifecycle refinement

- Reassignment
- Pending employee response
- Ticket linking

---

## 7. Risks and mitigations

### Risk: invalid status transitions create inconsistent workflow

Mitigation:
- Enforce transition rules in domain logic before persisting.
- Surface valid next states in validation errors.
- Add automated tests for every allowed and blocked transition.

### Risk: ticket visibility is not enforced

Mitigation:
- Separate internal and employee-visible notes at the model and API layer.
- Validate role checks before returning notes to clients.

### Risk: queue sorting is inconsistent or nondeterministic

Mitigation:
- Centralize queue ordering in one repository/service method.
- Sort deterministically by priority, then age, then ticket id as a final tie-breaker if needed.

### Risk: auto-close timing drifts or fires unexpectedly

Mitigation:
- Use a single background service with a clear timeout rule.
- Log timeout-based closures with a standardized reason.

---

## 8. Definition of done

The Technician story set is considered complete when all of the following are true:

- All Must Have stories pass their acceptance tests.
- All Should Have stories pass their acceptance tests.
- Could Have stories are either implemented or explicitly deferred with rationale.
- Ticket status transitions are auditable and validated.
- Queue ordering and employee/technician note visibility are verified.
- Background auto-close behavior is tested and logged.
- The UI and API are aligned to the same rules and error patterns.

---

## 9. Recommended implementation order

1. Domain rules and audit model
2. Assigned queue + status transitions
3. Close workflow and auto-close timeout
4. Unassigned ticket claim flow
5. Internal notes visibility
6. Reassignment, pending employee response, and linked tickets

This ordering minimizes risk while delivering a usable technician workflow early and extending functionality without destabilizing the core lifecycle.
