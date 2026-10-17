# Backend

## Run tests

From the repository root, run:

```powershell
dotnet test backend\KapeldinerService\KapeldinerService.slnx
```

The solution includes domain unit tests and an API integration test using
`WebApplicationFactory`. The API test exercises the test controller without
connecting to PostgreSQL, so the test suite does not require a running database
or Docker.

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
