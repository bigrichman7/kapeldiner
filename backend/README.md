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
