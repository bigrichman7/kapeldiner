# Authentication and administrator bootstrap

The API uses opaque, server-side sessions delivered in an HttpOnly cookie. The
cookie contains only a random session identifier; the serialized authentication
ticket is stored in PostgreSQL. Passwords use ASP.NET Core Identity's
`PasswordHasher<AuthUser>` rather than reversible encryption or plaintext.
There is no self-registration or user-management endpoint in this issue.

## Endpoints

| Method | Route | Access | Purpose |
|---|---|---|---|
| `GET` | `/api/auth/csrf` | Anonymous | Issue the antiforgery cookie and request token. |
| `POST` | `/api/auth/login` | Anonymous + CSRF token | Authenticate by login and password. |
| `GET` | `/api/auth/session` | Authenticated | Return the current user's id, login, and role. |
| `POST` | `/api/auth/logout` | Authenticated + CSRF token | Delete the server session and expire the cookie. |
| `GET` | `/api/admin/session` | Administrator | Example route protected by the administrator policy. |

For login, first fetch `/api/auth/csrf`, then send its `data.requestToken` in
`X-CSRF-TOKEN` on the login request. After successful login, fetch a fresh CSRF
token for subsequent authenticated `POST`, `PUT`, `PATCH`, and `DELETE`
requests. The antiforgery cookie is HttpOnly; the request token is returned
once in the JSON response. Responses use the standard
`{ "success", "data", "error" }` envelope.

The browser must send cookies with same-origin requests. Cross-origin credential
sharing is not configured by this service.

## Cookie and session settings

- Session lifetime is eight hours with sliding renewal.
- The session cookie is HttpOnly and `SameSite=Lax`.
- In Production, cookies use the `__Host-` prefix, `Secure=Always`, and `Path=/`
  (no `Domain` attribute). HTTPS is required.
- In Development, `Secure` follows the request scheme, allowing local HTTP
  development; prefer the HTTPS launch profile.
- The antiforgery cookie is HttpOnly, `SameSite=Strict`, and uses the same
  environment-specific secure policy.
- Session tickets are stored in the `auth_session` PostgreSQL table and removed
  on logout or expiry when accessed. Authentication users and their password
  hashes are in `auth_user`.
- In multi-instance deployments, persist and share the ASP.NET Core Data
  Protection key ring across instances and restarts; otherwise cookie
  validation can fail even though server-side sessions remain in PostgreSQL.

## Initial administrator

Apply the EF migration before starting the API:

```powershell
dotnet ef database update --project backend\KapeldinerService\src\KapeldinerService.Infrastructure.Postgres --startup-project backend\KapeldinerService\src\KapeldinerService.Web
```

The first administrator is inserted once from configuration. The username and
password are never shipped in tracked configuration files.

For local development, set ASP.NET Core User Secrets from the repository root:

```powershell
dotnet user-secrets set "Auth:Bootstrap:Admin:Login" "admin" --project backend\KapeldinerService\src\KapeldinerService.Web
dotnet user-secrets set "Auth:Bootstrap:Admin:Password" "<choose-a-local-password>" --project backend\KapeldinerService\src\KapeldinerService.Web
```

Local development has no default administrator credentials. If both settings
are omitted, no admin is created and the API can still start. Remove the
bootstrap secret after the account has been created.

In Production, provide both `Auth__Bootstrap__Admin__Login` and
`Auth__Bootstrap__Admin__Password` using the deployment secret manager or
environment configuration. The password must contain at least 12 characters.
If no administrator exists and either setting is missing, startup fails rather
than creating a known account. Once an administrator exists, the bootstrap
settings can be removed. Only one administrator role is allowed by a unique
database index.

The password field is passed directly to the password hasher and is never
logged. Bootstrap logs report only whether an administrator account was
created, not credentials.
