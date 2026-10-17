# Seat map and event seat state (MVP)

This document records the confirmed MVP requirements for the auditorium seat
map, events, users, quotas, and reservations. API implementation is the
priority over UI. This issue itself is limited to requirements and
documentation; it does not change application behavior.

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
- Rows `1` through `27` are the regular hall layout. Row `12А` is an optional
  additional row: an administrator enables it per event, either at event
  creation or later while reservations are open. Its displayed label is
  exactly `12А` (with the Cyrillic `А`). Without it, the event has 27 rows;
  with it, 28 row labels are displayed.
- The physical seat catalogue is shared, but row `12А` activation, seat
  availability, designations, quotas, and reservations are event-specific.
  Changes for one event must never alter another event's state.
- The workbook identifies 643 seats. The machine-readable transcription
  contains 643 distinct `(rowLabel, seatNumber)` pairs and retains their source
  worksheet coordinates. The optional `12А` seats are included in that shared
  catalogue and count toward an event's capacity only when that event enables
  the row.
- Users need to see only a seat's row label, seat number, and whether it is
  available. They do not see VIP/organization designations or who occupies a
  seat. VIP and organization seats are unavailable to users, indistinguishable
  from other unavailable seats. Administrators see designations and occupancy.
- There is one administrator in the MVP. Only the administrator creates
  users. A user's login and email are separate fields.
- A user's quota for an event is a pair `used_seats/available_seats`.
  `available_seats` is the capacity allocated to that user, not the remaining
  number of seats. An absent or zero-capacity quota is effectively `0/0` and
  does not grant event visibility. When creating an event, the administrator
  adds each participating user and must set that user's `available_seats`;
  for example, the initial quota can be `0/5`.
- Users see published events for which they have positive quota. A user can
  reserve only available seats and only as ordinary `booked` seats. A single
  confirmation sends the selected seats as one request. The reservation is
  committed immediately, without payment, and cannot exceed the user's
  allocated capacity. At `5/5`, no additional seat can be booked; cancelling
  one seat decrements `used_seats` by one.
- The administrator can enable the optional row, set/clear VIP and
  organization designations, and book any seat, including VIP/organization
  seats. The administrator can create, change, or cancel reservations for any
  user. Those reservations are attributed to the selected user and counted
  against that user's event quota; the same quota limit applies. The affected
  user sees the updated booked seats and `used_seats/available_seats` count.
- If the administrator lowers a user's `available_seats` below the current
  `used_seats`, the administrator must select which booked seats to cancel.
  The system must not choose seats for cancellation automatically, and the
  resulting `used_seats` must not exceed the new quota.
- Before an event starts, a user may change or cancel their own reservation;
  the administrator may change or cancel any user's reservation. A whole
  selection is submitted in one request. When a user's reservation contains
  no seats, delete the reservation record; create it again only when that user
  next confirms one or more seats.
- A reservation belongs to exactly one event and that event's user quota. It
  exists only while both the event and quota exist, and is deleted with either
  parent. A user with no reservation has no empty reservation record.
- Reservation changes are prohibited once an event has started and for past
  events. Event status changes to completed automatically after its end time.
- Administrators can see event statistics: seats used by each user, physical
  seats still free, and quota capacity/usage. Show event-wide totals separately
  from each user's quota: total seats enabled for the event, reserved seats,
  seats not reserved, seats currently bookable by users, and each user's
  `used_seats/available_seats`. An unreserved VIP/organization seat is not
  user-bookable.
- Use `Europe/Moscow` for event time. Display time in 24-hour form as
  `HH:mm, d MMMM yyyy` with a localized month name (for example,
  `18:30, 5 ноября 2026`).
- Use one response envelope for successful and failed API responses:
  `{"success": true|false, "data": ..., "error": ...}`. On success, `error`
  is `null`; on failure, `data` is `null` and `error` contains a stable code,
  a human-readable message, and optional field-level validation errors. Return
  appropriate HTTP status codes. Never return stack traces or internal
  exception details to users; log technical details server-side.
- API work takes priority over UI work.
- No application or API behavior is changed by this documentation issue.

## MVP model

The confirmed conceptual model is:

- `Seat`: stable `seatId`, `rowLabel`, `seatNumber`, and fixed `x`/`y`
  coordinates from `docs/hall-layout.json`.
- Event configuration contains whether optional row `12А` is enabled.
  Event-specific seat state is keyed by `(eventId, seatId)` and holds
  availability/designation independently from the shared seat catalogue.
- An event quota is keyed by `(eventId, userId)` and stores allocated
  `available_seats` and consumed `used_seats`.
- A reservation belongs to one event quota and contains one or more selected
  seats. It is committed immediately and is removed when its seat set becomes
  empty or its event/quota is deleted.
- Administrator-only user creation and administrator reservation management
  are part of the MVP. User self-registration is not. Administrator-created
  bookings are attributed to a user and consume that user's quota.

The API must support published-event discovery, event seat-map/state reads,
event creation and user quota assignment, event-scoped row activation and
seat designation, seat reservation, reservation change/cancellation, and
administrator event statistics with the permissions and time restrictions
above. The following REST-style routes and data shapes are the chosen MVP
contract for planning. Names can be mapped to the repository's implementation
conventions without changing the documented behavior.

### MVP routes and methods

All IDs in paths are opaque IDs. `{seatId}` identifies one seat from the shared
catalogue (for example, `12А-8`).

| Method | Route | Access | Purpose |
|---|---|---|---|
| `POST` | `/api/admin/users` | Administrator | Create a user with a distinct `login`, `email`, and `name`. |
| `GET` | `/api/events` | User | List published events where the current user has positive quota; include that user's quota. |
| `GET` | `/api/events/{eventId}` | User with event quota; administrator | Read event details visible to the caller. |
| `GET` | `/api/events/{eventId}/seats` | User with event quota | Read seat row/number and caller-safe `available`/`unavailable` state. |
| `GET` | `/api/admin/events` | Administrator | List and inspect events, including draft and completed events. |
| `POST` | `/api/admin/events` | Administrator | Create an event and assign initial user quotas in the same request. |
| `GET` | `/api/admin/events/{eventId}/seats` | Administrator | Read seats with designation, occupancy, and occupying user details. |
| `GET` | `/api/admin/events/{eventId}/statistics` | Administrator | Read event totals and per-user quota/usage statistics. |
| `PATCH` | `/api/admin/events/{eventId}` | Administrator | Update event details, including `endAt`, or enable optional row `12А`. Allowed until the event ends; completed events are immutable. |
| `POST` | `/api/admin/events/{eventId}/publish` | Administrator | Publish a draft event so eligible users can see it. |
| `PUT` | `/api/admin/events/{eventId}/quotas/{userId}` | Administrator | Add/update a user's quota, with explicit seat cancellations when lowering capacity below usage. |
| `PUT` | `/api/admin/events/{eventId}/seats/{seatId}/designation` | Administrator | Set designation to `vip`, `organization`, or `none`. |
| `PUT` | `/api/events/{eventId}/my-reservation` | User with event quota | Replace the caller's full reserved-seat selection in one request. |
| `DELETE` | `/api/events/{eventId}/my-reservation` | User with event quota | Cancel the caller's complete reservation in one request. |
| `PUT` | `/api/admin/events/{eventId}/quotas/{userId}/reservation` | Administrator | Replace the selected user's full reserved-seat selection in one request. |
| `DELETE` | `/api/admin/events/{eventId}/quotas/{userId}/reservation` | Administrator | Cancel the selected user's complete reservation in one request. |

`POST /api/admin/events` accepts `title`, optional `description`, `startAt`,
`endAt`, optional `enableAdditionalRow12A`, and `quotas: [{userId,
availableSeats}]`. Quota assignment for users added later uses the quota
endpoint. A zero quota does not make an event visible to that user.

`PUT .../my-reservation` and the administrator counterpart accept
`{"seatIds": ["..."]}`. The array is the complete desired selection, not a
delta: adding, replacing, or removing selected seats is atomic. An empty array
has the same effect as cancellation. A reservation record exists only while
the selection is non-empty. A request that changes no seats may return success
without changing quota usage.

The quota endpoint accepts
`{"availableSeats": 5, "cancelSeatIds": ["..."]}`. If the new capacity is
less than current usage, `cancelSeatIds` must explicitly select that user's
currently reserved seats to cancel so the final usage fits the new capacity.
The quota update and selected cancellations succeed or fail together. If
capacity is not being reduced below usage, `cancelSeatIds` may be omitted or
empty.

For a published event's start time and before its end time, users may only
read; reservation writes return `409 EVENT_NOT_BOOKABLE`. The administrator
may edit the event, quotas, designations, and reservations after start and up
to (but not including) the end time. The administrator may extend `endAt`
while the event is running. At `endAt`, the event automatically becomes
`completed`; a completed event cannot be edited or booked by anyone. Automatic
completion must be performed by a background scheduler/worker and must also
be enforced on API reads/writes when the stored status has not yet caught up.
The event time and boundary are interpreted in `Europe/Moscow`.

### Validation, concurrency, and response contract

- Trim required string inputs and reject empty values. Use basic size limits:
  user `name` 1–100 characters, event `title` 1–200, `login` 3–50, and
  `email` at most 254. Validate email with a conventional email-format check;
  login must be unique and contain only letters, digits, `.`, `_`, or `-`.
  Optional `description` is limited to 2,000 characters.
- `startAt` and `endAt` are required valid timestamps; `endAt` must be later
  than `startAt`. Quota and seat selections must be non-negative/valid,
  duplicate seat IDs in one request are invalid, and referenced users,
  events, and seats must exist.
- Seat acquisition is atomic. The request that first commits a seat in the
  database succeeds; a concurrent request for the same seat fails with
  `409 SEAT_ALREADY_BOOKED`. The losing client refreshes the event seat map.
  Validate and commit the complete seat selection and quota usage in one
  transaction so a partially applied request cannot occur.
- A designation cannot be assigned to a seat that is already reserved;
  reservation and designation changes must be atomic to prevent conflicts.
- Successful responses use HTTP `2xx` and the same envelope, e.g.
  `{"success":true,"data":{"eventId":"..."},"error":null}`. Errors use the
  appropriate non-2xx status and envelope, e.g.
  `{"success":false,"data":null,"error":{"code":"SEAT_ALREADY_BOOKED","message":"Место уже занято. Обновите схему зала.","fields":null}}`.
- Use `400` for malformed/invalid input, `401` for unauthenticated callers,
  `403` for insufficient role/permission, `404` for missing or invisible
  resources, `409` for conflicts (including an occupied seat), and `500` for
  unexpected server failures. For field validation errors, `error.fields` is
  an optional map from field name to a list of human-readable messages.
- Keep response envelopes and field naming consistent across endpoints.
  Return only human-readable error messages and safe error codes to clients;
  log exception details and correlation/trace IDs server-side, never stack
  traces in API responses.

## Workbook discrepancy

The workbook has 28 labelled rows in this order: `1`–`11`, `12А`, `12`, and
`13`–`27`. The 27 regular rows plus optional `12А` explain the brief's
statement that the hall has 27 rows. All 643 seat cells, including the
additional row, are transcribed. `12А` is enabled separately for each event;
its workbook spelling is preserved as the user-facing label.

The workbook's worksheet cell grid is used as a source coordinate reference:
`x` is the 1-based Excel column number and `y` is the 1-based Excel row number.
These are layout positions, not measurements in physical units. `sourceCell`
allows the coordinates to be traced back to the workbook.

## Open decisions before API implementation

The core MVP routes, validation approach, response envelope, booking conflict,
and event time rules are specified above. Implementation may still choose
framework-specific status/error plumbing, database locking/constraint
mechanisms, and scheduler technology, provided the external behavior remains
the same. Broader authentication/login details and any features beyond one
administrator and administrator-created users remain outside this seat-map
MVP.

The workbook-grid `x`/`y` values are source references for the catalogue, not
customer-facing fields. The user-facing map needs only row labels and seat
numbers; a future API/UI may use normalized internal coordinates without
changing the displayed identifiers.

## Current implementation is not the contract

The existing domain `Seat` validates row numbers up to 27 and seat numbers up
to 25, and `SeatType` combines `booked`, `organization`, and `vip` in one
enum. The workbook includes seat number 26 and the optional `12А` row, so
those integer limits do not describe the full event-configurable layout. The
existing domain also does not cover all confirmed quota and reservation
requirements. These are observations about current code, not endorsements of
its model; changing the domain model or API is out of scope for this issue.
