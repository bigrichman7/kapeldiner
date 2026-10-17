# Backend runbook and API

This guide describes the current implementation in `KapeldinerService`; it is
not a replacement for product requirements. QR/electronic tickets are not
implemented. See [Authentication](../docs/authentication.md) for session,
CSRF, cookie, and administrator-bootstrap details and
[the product specification](../docs/product-spec.md) for the confirmed hall
and MVP requirements.

## Run locally

Prerequisites: the .NET SDK selected by `global.json` (10.0.203 or a later
feature band), Docker with Compose, and PowerShell. Commands below run from the
repository root. No production credentials or tracked settings are required.

1. Create a root `.env` file (it is ignored by Git) with a unique password for
   this local database only:

   ```dotenv
   POSTGRES_DB=kapeldiner_service_db
   POSTGRES_USER=kapeldiner_local
   POSTGRES_PASSWORD=<replace-with-a-local-only-password>
   POSTGRES_PORT=5435
   ```

2. Load those values into the current PowerShell process, start PostgreSQL,
   and store the connection string in this project's .NET User Secrets. Replace
   the placeholder in `.env` before running these commands:

   ```powershell
   $db = Get-Content .env | ConvertFrom-StringData
   $env:POSTGRES_DB = $db.POSTGRES_DB
   $env:POSTGRES_USER = $db.POSTGRES_USER
   $env:POSTGRES_PASSWORD = $db.POSTGRES_PASSWORD
   $env:POSTGRES_PORT = $db.POSTGRES_PORT
   $env:ASPNETCORE_ENVIRONMENT = "Development"
   docker compose -f backend\KapeldinerService\docker-compose.yml up -d postgres

   dotnet user-secrets set "ConnectionStrings:KapeldinerServiceDb" `
     "Host=localhost;Port=$($db.POSTGRES_PORT);Database=$($db.POSTGRES_DB);Username=$($db.POSTGRES_USER);Password=$($db.POSTGRES_PASSWORD)" `
     --project backend\KapeldinerService\src\KapeldinerService.Web
   ```

   Docker Compose reads the same root `.env`; the port exposed on the host is
   `POSTGRES_PORT` (5435 above). User Secrets override the connection string
   from `appsettings.json`. Never put a real deployment connection string or
   production credential in `.env`, tracked settings, or examples.

3. Install the EF CLI if it is not already available, restore dependencies, and
   apply all pending PostgreSQL migrations:

   ```powershell
   dotnet tool install --global dotnet-ef --version 10.0.7
   dotnet restore backend\KapeldinerService\KapeldinerService.slnx
   dotnet ef database update `
     --project backend\KapeldinerService\src\KapeldinerService.Infrastructure.Postgres\KapeldinerService.Infrastructure.Postgres.csproj `
     --startup-project backend\KapeldinerService\src\KapeldinerService.Web\KapeldinerService.Web.csproj `
     --context KapeldinerServiceDbContext
   ```

   The EF CLI install is a one-time step; skip it when `dotnet ef --version`
   already works. The migrations in this checkout are `Initial`,
   `EventItemAddedProperties`, `EventQuotaEditColumn`, `AddAuthentication`,
   `AddUserProfile`, `AtomicReservations`, `AddUniqueEventQuotaPerUser`,
   `CorrectSeatLocationConstraints`, and `EventHistoryIndexes`. `database
   update` applies them in migration order and records them in EF's migration
   history table. Run it again after pulling schema changes, before starting
   the updated API. Do not edit an already-applied migration to change a
   database; add a new migration instead.

4. Configure a development administrator using a local-only password. There
   is no default administrator account or password in the repository:

   ```powershell
   dotnet user-secrets set "Auth:Bootstrap:Admin:Login" "admin" `
     --project backend\KapeldinerService\src\KapeldinerService.Web
   dotnet user-secrets set "Auth:Bootstrap:Admin:Password" "<choose-a-unique-local-password>" `
     --project backend\KapeldinerService\src\KapeldinerService.Web
   ```

   Replace the password placeholder with a unique local password. In
   Development, the API also starts without these two settings, but it will
   have no administrator. When both values are present and no administrator
   exists, the hosted bootstrapper creates one on startup; remove both secrets
   after that account exists. Production requires both settings when booting
   the first administrator and enforces a password of at least 12 characters.
   See [administrator bootstrap](../docs/authentication.md#initial-administrator)
   for account details.

5. Start the API with the HTTPS Development profile:

   ```powershell
   dotnet dev-certs https --trust
   dotnet run --project backend\KapeldinerService\src\KapeldinerService.Web `
     --launch-profile https
   ```

   The profile serves `https://localhost:7282` and `http://localhost:5089`.
   The root endpoint is `/`; health is `/api/health`. In Development only,
   OpenAPI JSON is at `/openapi/v1.json` and Scalar UI at `/scalar/v1`.
   The application requires the migrated PostgreSQL database to start.

6. Run the automated suite from the repository root:

   ```powershell
   dotnet test backend\KapeldinerService\KapeldinerService.slnx
   ```

   API tests use SQLite and do not need Docker. The PostgreSQL reservation
   concurrency test runs only when `KAPELDINER_TEST_POSTGRES_CONNECTION` points
   at a disposable test database.

### Optional email for local user creation

Creating an account through `POST /api/admin/users` sends a generated
permanent password by SMTP. SMTP is not needed to start the API or exercise
the other endpoints. Without SMTP, account creation returns
`503 EMAIL_NOT_CONFIGURED`; if delivery fails it returns
`502 EMAIL_DELIVERY_FAILED`, and in both cases the database insert is rolled
back. Configure local SMTP in User Secrets if you need to test that operation:

```powershell
dotnet user-secrets set "Email:Smtp:Host" "<local-smtp-host>" --project backend\KapeldinerService\src\KapeldinerService.Web
dotnet user-secrets set "Email:Smtp:Port" "<local-smtp-port>" --project backend\KapeldinerService\src\KapeldinerService.Web
dotnet user-secrets set "Email:Smtp:FromAddress" "<sender-address>" --project backend\KapeldinerService\src\KapeldinerService.Web
```

Optional `Email:Smtp:FromName` defaults to `Kapeldiner`. SMTP authentication
is either disabled or configured with both `Email:Smtp:UserName` and
`Email:Smtp:Password`; STARTTLS is always enabled. Do not use a production
mailbox or credentials for local development. For the full credential and
delivery behavior, see [authentication documentation](../docs/authentication.md).

## API conventions

- Unless noted below, endpoints return the JSON envelope
  `{"success":true,"data":...,"error":null}` or
  `{"success":false,"data":null,"error":{"code":"...","message":"...","fields":null}}`.
  Property names are camel-cased. Validation errors may include a `fields`
  object whose values are arrays of messages.
- Authentication is a server-side session in an HttpOnly cookie. There is no
  bearer-token API, public registration, or role-change endpoint. The roles
  are `Administrator` and `User`.
- Obtain the CSRF request token from `GET /api/auth/csrf`; preserve the cookie
  and send `data.requestToken` in `X-CSRF-TOKEN` on login and on every
  authenticated `POST`, `PUT`, `PATCH`, or `DELETE`. After login, obtain a
  fresh token. Read requests do not require the header.
- User event and reservation routes require role `User`, a published event,
  and a positive quota on that event. Missing or invisible events are
  returned as `404`, rather than exposing their existence.
- Event timestamps are accepted as ISO-8601 timestamps with an offset,
  persisted as UTC instants, and returned with the `Europe/Moscow` offset.
  Seat identifiers use the `row-seat` form, e.g. `1-2` or `12А-8`.
- Reservation `PUT` replaces the complete seat selection atomically; it is
  not a delta. An empty `seatIds` array clears the reservation. Reservation
  records are removed when empty.

## Endpoints

`{eventId}` and `{userId}` are GUIDs. `{seatId}` is a seat identifier such as
`12А-8`. A CSRF header is required on unsafe cookie-authenticated requests as
described above.

| Method | Route | Access | Purpose |
|---|---|---|---|
| `GET` | `/api/health` | Anonymous | ASP.NET Core health-check response. |
| `GET` | `/api/auth/csrf` | Anonymous | Set antiforgery cookie; return request token. |
| `POST` | `/api/auth/login` | Anonymous + CSRF | Authenticate using login and password. |
| `GET` | `/api/auth/session` | Authenticated | Return current user `id`, `login`, and `role`. |
| `POST` | `/api/auth/logout` | Authenticated + CSRF | End the current session. |
| `GET` | `/api/admin/session` | Administrator | Check administrator access. |
| `GET`, `PUT` | `/api/users/me` | Authenticated | Read/update own login, name, and email. |
| `GET` | `/api/admin/users` | Administrator | List user profiles. |
| `GET`, `PUT`, `DELETE` | `/api/admin/users/{id}` | Administrator | Read, update, or delete a user. |
| `POST` | `/api/admin/users` | Administrator | Create a `User` account and email its generated password. |
| `GET` | `/api/events` | User | List published events with a positive personal quota. |
| `GET` | `/api/events/{eventId}` | User with quota | Read a visible published event. |
| `GET` | `/api/events/{eventId}/seats` | User with quota | Read seat IDs and caller-visible `available` state. |
| `GET`, `PUT`, `DELETE` | `/api/events/{eventId}/my-reservation` | User with quota | Read, replace, or cancel own reservation. |
| `GET` | `/api/admin/events` | Administrator | List/filter/paginate events, including all statuses. |
| `GET` | `/api/admin/events/{eventId}` | Administrator | Read event details, including quotas. |
| `POST` | `/api/admin/events` | Administrator | Create a draft event with optional initial quotas. |
| `PATCH` | `/api/admin/events/{eventId}` | Administrator | Update details and optional-row setting. |
| `POST` | `/api/admin/events/{eventId}/publish` | Administrator | Publish a draft event. |
| `POST` | `/api/admin/events/{eventId}/cancel` | Administrator | Cancel a draft or published event. |
| `GET` | `/api/admin/events/{eventId}/seats` | Administrator | Read seats, designation, booking state, and occupant. |
| `PUT` | `/api/admin/events/{eventId}/seats/{seatId}/designation` | Administrator | Set `vip`, `organization`, or `none`. |
| `GET` | `/api/admin/events/{eventId}/reservations` | Administrator | List event reservations. |
| `GET` | `/api/admin/events/{eventId}/statistics` | Administrator | Read seat and per-user quota totals. |
| `GET` | `/api/admin/events/{eventId}/quotas` | Administrator | List event quotas. |
| `GET`, `PUT`, `DELETE` | `/api/admin/events/{eventId}/quotas/{userId}` | Administrator | Read, create/update, or delete a quota. |
| `GET`, `PUT`, `DELETE` | `/api/admin/events/{eventId}/quotas/{userId}/reservation` | Administrator | Read, replace, or cancel a user's reservation. |

`GET /` returns a plain-text readiness message. `/api/test` is an unauthenticated
test endpoint. These operational/test endpoints, health-check output,
automatic model-binding errors, and unhandled server exceptions are not
covered by the API envelope guarantee.

## Request and response examples

Examples below show request bodies and response `data` objects; use your own
identifiers and dates. Keep the session cookie between calls and follow the
CSRF flow above.

**Log in** (fetch a token first; include the cookie and header):

```http
POST /api/auth/login
Content-Type: application/json
X-CSRF-TOKEN: <requestToken>

{"login":"admin","password":"<local-password>"}
```

```json
{"success":true,"data":{"id":"<user-guid>","login":"admin","role":"Administrator"},"error":null}
```

**Create an event and initial quotas** (`startAt` must be in the future;
omitting `quotas` creates no user-visible event):

```http
POST /api/admin/events
Content-Type: application/json
X-CSRF-TOKEN: <requestToken>

{
  "title":"Evening performance",
  "description":"Optional event description",
  "startAt":"2030-11-05T18:30:00+03:00",
  "endAt":"2030-11-05T20:30:00+03:00",
  "enableAdditionalRow12A":false,
  "quotas":[{"userId":"<user-guid>","availableSeats":5}]
}
```

The event is created as `draft`; publish it with
`POST /api/admin/events/{eventId}/publish`. Only initial quotas with a
positive `availableSeats` are stored; zero-capacity entries are ignored. Each
initial user must already exist and be a `User`.

**Create a user** (all properties are required; login is 3–50 letters/digits,
periods, underscores, or hyphens; email is unique; name is 1–100 characters):

```json
{"login":"guest.1","email":"guest@example.test","name":"Guest One"}
```

Success is `201 Created` with the profile in `data`. Role is always `User`;
passwords and roles cannot be set in this request. A newly generated
permanent password is delivered by email, never returned by the API.

**Replace a reservation** (full desired selection, with no duplicate seat IDs):

```http
PUT /api/events/<event-guid>/my-reservation
Content-Type: application/json
X-CSRF-TOKEN: <requestToken>

{"seatIds":["1-2","1-3"]}
```

The response data contains `reservationId`, `eventId`, `userId`, sorted
`seatIds`, `availableSeats`, `usedSeats`, and `remainingSeats`. `DELETE` on
the same path cancels all seats. The administrator can use the corresponding
`/api/admin/events/{eventId}/quotas/{userId}/reservation` routes for a
selected user.

**Update quota**:

```json
{"availableSeats":3,"cancelSeatIds":["1-2","1-3"]}
```

`cancelSeatIds` must contain exactly the number of that user's currently
reserved seats required to bring usage within the new capacity. The quota and
cancellations are committed together. Quota `PUT` accepts 0 to 643; a quota
with reservations cannot be deleted. Initial event creation differs: it
ignores zero-capacity quota entries and currently validates only that the
initial capacity is non-negative (it does not apply the 643-seat maximum).

**Set a seat designation**:

```json
{"designation":"vip"}
```

Use `"organization"` for organization seats or `"none"` to clear the
designation. The selected seat must belong to the event's enabled layout and
cannot be designated while booked.

**Administrator event listing** supports:

- `period=all|upcoming|past` (default `all`); upcoming means `startAt` is
  later than now, and past means `endAt` is at or before now.
- `search` matches title or description (maximum 200 characters).
- `status=draft|published|completed|cancelled`.
- `from` and `to` are inclusive ISO-8601 bounds on `startAt`; `from` may not
  be later than `to`.
- `sortBy=startAt|endAt|title` (default `startAt`),
  `sortOrder=asc|desc` (default `asc`); event ID is the tie-breaker.
- `page` (default 1), `pageSize` (default 50, maximum 100). The response is
  still an array in `data`; `X-Total-Count`, `X-Page`, and `X-Page-Size`
  response headers describe the page.

## Authorization, timing, and errors

- Anonymous callers can only obtain a CSRF token and log in. Authenticated
  callers can read/update their own profile; account administration, event
  management, quotas, seat designations, all-user reservations, and statistics
  require `Administrator`.
- A `User` can see only published events with a positive quota and can only
  read/book their own event reservation. Zero quota does not grant visibility.
- User reservations are accepted only for a published event before its start.
  Reservation replacement/cancellation, quota edits, and designation changes
  are also rejected from the event start onward, including for administrators.
  Quota/designation failures use `409 EVENT_NOT_EDITABLE`; reservation writes
  use `409 EVENT_NOT_BOOKABLE`.
- Administrators may edit event details while the event is running if they
  leave its start time unchanged and set an end time still in the future.
  Canceled and completed events cannot be edited. Events become `completed`
  at/after `endAt`; a background service checks once per minute, and event
  operations also persist missed completions.
- The common explicit error statuses are `400` validation/CSRF failure
  (`VALIDATION_FAILED`, `CSRF_VALIDATION_FAILED`),
  `401` unauthenticated or invalid login, `403` wrong role, `404` missing or
  invisible event/user/quota, and `409` state or concurrency conflict.
  Resource errors use `EVENT_NOT_FOUND`, `USER_NOT_FOUND`, or
  `QUOTA_NOT_FOUND`; an event with no positive user quota is intentionally
  reported as `EVENT_NOT_FOUND` to that user.
  Account creation additionally returns `503 EMAIL_NOT_CONFIGURED` and
  `502 EMAIL_DELIVERY_FAILED`. Common conflict codes include
  `SEAT_ALREADY_BOOKED`, `SEAT_UNAVAILABLE`, `QUOTA_EXCEEDED`,
  `QUOTA_HAS_RESERVATIONS`, `QUOTA_UPDATE_CONFLICT`,
  `QUOTA_ALREADY_EXISTS`, `QUOTA_USAGE_INCONSISTENT`,
  `RESERVATION_CONFLICT`, `SEAT_UPDATE_CONFLICT`,
  `EVENT_NOT_BOOKABLE`, `EVENT_NOT_EDITABLE`, `EVENT_START_ALREADY_REACHED`,
  and `ADDITIONAL_ROW_IN_USE`.
- User/profile conflicts include `LOGIN_ALREADY_EXISTS`,
  `EMAIL_ALREADY_EXISTS`, `USER_UNIQUE_CONFLICT`, and
  `ADMINISTRATOR_PROTECTED`. Event status conflicts include
  `EVENT_NOT_PUBLISHABLE` and `EVENT_NOT_CANCELLABLE`.
- `POST /api/auth/login` returns `400 INVALID_CREDENTIALS` for missing or
  malformed fields and `401 INVALID_CREDENTIALS` when the credentials do not
  match. Protected routes return `401 UNAUTHENTICATED` or `403 FORBIDDEN`.
- Event status is automatically completed after end time; the worker does
  not send notifications. Event creation starts in `draft`, and only drafts
  may be published. Admin list filters and detail reads include canceled and
  completed records; user-facing event reads do not.

## Implemented behavior that differs from product requirements

Requirements in [the product spec](../docs/product-spec.md) are not proof
that a behavior exists. In the current code:

- The product requirements allow administrators to change reservations,
  quotas, and designations after event start but before event end. The
  implementation rejects all three from the start (`EVENT_NOT_BOOKABLE` for
  reservations, `EVENT_NOT_EDITABLE` for quotas/designations). Event detail
  edits are a separate case: they may continue after start with the original
  start time unchanged and a future end time.
- Event creation stores only positive initial quotas. A zero-capacity quota
  sent in the create request is ignored; a later quota `PUT` can store zero.
- Quota `PUT` enforces a maximum of 643 seats, but initial quotas supplied
  during event creation currently have no matching maximum validation.
- Account creation depends on an SMTP server and sends a permanent password
  by email. Without SMTP, the user is not created. This is not a local
  no-mail account-creation flow.
- Explicit controller/authentication/CSRF errors use the envelope. The
  framework health check, root/test endpoints, automatic model-binding
  failures, and unhandled exceptions do not have a guaranteed common
  envelope or stable API error contract.

## Future feature: QR/electronic tickets (not implemented)

No QR code, electronic ticket, ticket delivery, or admission-scanning API is
implemented. Decide and approve these criteria before implementation:

- **Ticket lifecycle and ownership:** when a ticket is created (reservation,
  payment, or another action), whether one ticket represents a reservation or
  one seat, how transfers/reissues/cancellations work, and whether the ticket
  remains valid after a seat or reservation change.
- **Admission policy:** what event/staff roles may scan, whether tickets are
  single-use, how duplicate scans and offline scanners behave, how check-in
  is reversed, and what time window or event status allows entry.
- **QR payload and verification:** use a random, non-enumerable, revocable
  token rather than personal data or a sequential reservation ID; specify
  signature/key rotation or server lookup, expiry, replay prevention, and
  what the scanner displays for valid, invalid, revoked, and already-used
  tickets.
- **Delivery and recovery:** whether the ticket appears in a user account,
  email, or both; how users retrieve it without exposing another user's
  ticket; and what happens if email is unavailable or a ticket is lost.
- **Persistence and privacy:** define ticket/check-in records, retention and
  deletion policy, audit trail, data minimization, and whether the ticket
  contains any personal information (it should not).
- **Acceptance criteria:** tests should prove only an authorized scanner can
  validate; a valid ticket admits at most once unless an approved policy says
  otherwise; cancelled/replaced reservations cannot be admitted; tampered,
  expired, unknown, and revoked tokens fail safely; repeated concurrent scans
  have a deterministic result; and QR content exposes no seat-holder data.

Until those decisions are approved, keep QR/electronic tickets out of the MVP
and do not treat a reservation response as an issued or scannable ticket.
