# Backend

## Run tests

From the repository root, run:

```powershell
dotnet test backend\KapeldinerService\KapeldinerService.slnx
```

The solution includes domain unit tests and API integration tests using
`WebApplicationFactory` and SQLite, so the standard test suite does not require
a running database or Docker. The PostgreSQL concurrency test is optional and
skipped unless `KAPELDINER_TEST_POSTGRES_CONNECTION` points to a disposable
test database.

## Events

Administrators manage events through `GET /api/admin/events`,
`GET /api/admin/events/{eventId}`, `POST /api/admin/events`,
`PATCH /api/admin/events/{eventId}`, and the
`POST /api/admin/events/{eventId}/publish` and `/cancel` actions. Event
creation accepts an optional initial `quotas` array; it is used only to grant
event visibility and does not expose later quota management.

Users can list published events assigned to them with a positive quota using
`GET /api/events`, and read those same events at `GET /api/events/{eventId}`.
Responses include `bookingOpen`, which is true only before the start of a
published event. Event timestamps are stored as UTC instants and returned with
the `Europe/Moscow` offset.

An event-completion worker persists the `completed` status once an event ends,
checking once per minute. Event API reads and writes also persist any missed
completions before continuing, so status and visibility remain correct if the
worker was delayed or unavailable.

## Reservations

Users with an event quota can read and replace their confirmed reservation with
`GET` and `PUT /api/events/{eventId}/my-reservation`; `DELETE` on the same path
cancels it. The `PUT` body is `{ "seatIds": ["1-2", "1-3"] }`. A replacement
takes effect immediately and may contain no more seats than the user's quota.
Bookings, replacements, and cancellations are available only for published
events before their start time.

`GET /api/events/{eventId}/seats` returns availability for the event's hall
layout. Place identifiers are row-seat pairs such as `1-2`; availability and
VIP/organization designations are event-specific. Ordinary users cannot book
designated seats. Administrators can inspect reservations and event statistics
with `GET /api/admin/events/{eventId}/reservations` and
`GET /api/admin/events/{eventId}/statistics`, manage a user's booking with
`GET`, `PUT`, and `DELETE /api/admin/events/{eventId}/quotas/{userId}/reservation`,
and inspect/designate seats with
`GET /api/admin/events/{eventId}/seats` and
`PUT /api/admin/events/{eventId}/seats/{seatId}/designation`. Reservation and
seat-designation changes are rejected from the event start onward. Quota edits
also stop at the event start; reducing a quota below its current usage requires
explicit `cancelSeatIds` so the selected bookings and quota are updated
together.

PostgreSQL enforces one booking per event and seat. Reservation changes and
quota usage are persisted in the same transaction. After updating the
application, apply the new schema migrations from the repository root with:

```powershell
dotnet ef database update --project backend\KapeldinerService\src\KapeldinerService.Infrastructure.Postgres\KapeldinerService.Infrastructure.Postgres.csproj --startup-project backend\KapeldinerService\src\KapeldinerService.Web\KapeldinerService.Web.csproj --context KapeldinerServiceDbContext
```

The PostgreSQL concurrency integration test is skipped unless
`KAPELDINER_TEST_POSTGRES_CONNECTION` points to a disposable test database.
Provide that connection string when running the suite to verify concurrent
attempts against PostgreSQL.
