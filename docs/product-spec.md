# Seat map and event seat state (MVP)

This document records the current MVP requirements for the auditorium seat
map, events, users, and reservations. API implementation is the priority over
UI. This issue itself is limited to requirements and documentation; it does
not change application behavior.

## Sources and precedence

1. Decisions explicitly confirmed by the product owner take precedence.
2. Issue [#6](https://github.com/bigrichman7/kapeldiner/issues/6) records
   requested outcomes, but does not settle every behavior or API detail.
3. `docs/hall-layout.json` is a transcription of `hall.xlsx`, sheet `Лист3`.
   The workbook is the source for physical seat identifiers and positions.
4. Existing backend code and older endpoint/domain notes describe current
   implementation or proposals; they are not authoritative product decisions.

The `product-spec.md`, `Endpoints.md`, and `Предметная область.md` documents
referenced during issue discovery were not present in this repository worktree.
The product owner confirmed the requirements below from the main checkout.
This file records those confirmed requirements and makes remaining decisions
explicit instead of treating missing or possibly stale auxiliary documents as
approved requirements.

## Confirmed requirements

- The auditorium has one shared, fixed physical seat map.
- Reservations, seat availability, VIP designation, and organization
  designation are independent for each event. An event's changes must never
  alter another event's state.
- The workbook identifies 643 seats. The machine-readable transcription
  contains 643 distinct `(rowLabel, seatNumber)` pairs and retains their source
  worksheet coordinates.
- Seat identifiers and geometry belong to the shared seat catalogue.
  Reservation and designation state belong to an event and a seat, not to the
  shared catalogue.
- There is one administrator in the MVP. Only the administrator creates
  users. A user's login and email are separate fields.
- Users can see published events and reserve seats within the applicable
  event quota. A successful reservation is committed immediately.
- Before an event starts, a user may change or cancel their own reservation;
  the administrator may change or cancel any reservation.
- Reservation changes are prohibited once an event has started and for past
  events. Event status changes to completed automatically after its end time.
- API work takes priority over UI work.
- No application or API behavior is changed by this documentation issue.

## MVP model

The confirmed conceptual model is:

- `Seat`: stable `seatId`, `rowLabel`, `seatNumber`, and fixed `x`/`y`
  coordinates from `docs/hall-layout.json`.
- Event-specific state is keyed by `(eventId, seatId)`. Reservation state and
  VIP/organization designation are separate concepts; neither is a global
  property of a physical seat.
- A reservation belongs to a user and an event, and contains its reserved
  seats. It is committed immediately, subject to the event quota.
- Administrator-only user creation and administrator reservation rights are
  part of the MVP. User self-registration is not.

The API must support published-event discovery, event seat-map/state reads,
seat reservation, and reservation change/cancellation with the permissions
and time restrictions above. Exact paths, HTTP methods, request/response
schemas, and error representations are not specified by the confirmed
requirements; do not infer them from potentially stale endpoint notes.

## Workbook discrepancy

The workbook has 28 labelled seating rows in this order:
`1`–`11`, `12А`, `12`, and `13`–`27`. The workbook title says 643 seats, and
the 643 enumerated seat cells agree with that total. This does not agree with
the brief's statement that the workbook has 27 rows. The transcription
preserves both identifiers and retains the original cell text in
`sourceRowLabel`, rather than silently merging or renumbering `12А` and `12`.
Confirm whether `12А` is a separate physical row or a labeling error before
treating the row count/labels as final.

The workbook's worksheet cell grid is used as a source coordinate reference:
`x` is the 1-based Excel column number and `y` is the 1-based Excel row number.
These are layout positions, not measurements in physical units. `sourceCell`
allows the coordinates to be traced back to the workbook.

## Open decisions before API implementation

1. Confirm whether `12А` is a distinct physical row, a renamed row, or a
   workbook labeling error; consequently confirm whether the hall has 27 or 28
   rows. Do not merge or rename it without confirmation.
2. Confirm the API's row/seat identifier representation, including how to
   represent a row label with a letter suffix.
3. Define event quota semantics, including its scope and how a quota limits
   reservation changes.
4. Define reservation status/lifecycle and customer-visible states, including
   whether any intermediate hold or expiry state exists. The confirmed rule
   is that a successful reservation is immediate.
5. Define whether VIP seats can be reserved by customers, whether organization
   seats are unavailable to customers, and which designation/reservation
   combinations are valid. Define the atomic conflict rule for concurrent
   reservation and designation changes.
6. Define authentication/authorization details beyond the confirmed single
   administrator and administrator-managed user creation, and the exact
   occupier information administrators may see.
7. Confirm route names, HTTP methods, request/response schemas, error behavior,
   and whether event-seat state is initialized or derived from reservations.
8. Define the event time-zone convention and boundary behavior for starting,
   ending, and automatically completing an event.
9. Confirm whether the transcribed worksheet coordinates are the desired
   presentation coordinates or whether a cleaned/normalized coordinate system
   is required.

## Current implementation is not the contract

The existing domain `Seat` validates row numbers up to 27 and seat numbers up
to 25, and `SeatType` combines `booked`, `organization`, and `vip` in one
enum. The workbook includes seat number 26 and row label `12А`, so those
integer limits do not describe the supplied layout. The existing domain also
does not cover the confirmed user and reservation lifecycle requirements.
These are observations about current code, not endorsements of its model;
changing the domain model or API is out of scope for this issue.
