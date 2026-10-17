# Kapeldiner frontend

The frontend is a small React + TypeScript application. It uses the existing
ASP.NET Core API, session cookies, and the API's CSRF protection. There are no
demo accounts or mock API responses.

## Run locally

1. Start the API and its PostgreSQL database by following the repository's
   [backend runbook](../backend/README.md#run-locally). Keep the API's HTTPS
   Development profile on `https://localhost:7282`.
2. In another terminal, from `frontend/`, install dependencies and start Vite:

   ```powershell
   npm ci
   npm run dev
   ```

3. Open the local URL printed by Vite (normally `http://localhost:5173`).
   Vite proxies `/api` to the local HTTPS API. The proxy is development-only;
   this does not change production API hosting or authentication.

The first administrator must already be bootstrapped as described in the
backend runbook. User creation uses the existing email flow: the API must have
SMTP configured to deliver a generated password, otherwise it returns an
explicit error and does not create the account.

## Verify

```powershell
npm run check
npm test
npm run build
```

The event map is rendered from the shared catalogue in `docs/hall-layout.json`.
Availability, reservations, event data, and seat designations are always read
from the API; user responses intentionally omit occupant and VIP details.
