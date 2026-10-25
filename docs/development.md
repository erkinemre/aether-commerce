# Development

Requirements: .NET 10 SDK, Docker Compose with Linux containers, Python 3. There is no Node dependency.

Copy `.env.example` to `.env`, replace placeholders with random independent values, and run `docker compose up --build -d`. API ports are 5101 and 5102; PostgreSQL is on loopback 5432. Stop with `docker compose stop` to preserve state. Initialization runs only once on an empty database volume; never delete an existing volume as a routine upgrade step.

Native builds:

```sh
dotnet build src/Aether.Orders.Api -c Release
dotnet build src/Aether.Inventory.Api -c Release
python tests/run_security.py
```

For native hosting set `ConnectionStrings__Orders` or `ConnectionStrings__Inventory` to the matching initialized database. Set `Security__ClientKey`, `Security__ServiceKey`, `Security__TenantId`, and `InventoryBaseUrl` for Orders; Inventory requires `Security__ServiceKey`. `ASPNETCORE_URLS` selects the listen address. Start with `dotnet run --project src/Aether.Orders.Api --no-launch-profile` and the equivalent Inventory command in another shell.

Security tests start temporary processes on 15101/15102, generate ephemeral credentials, use an unreachable database and terminate processes on completion. Full integration tests use 5101/5102 by default, or `ORDERS_URL`/`INVENTORY_URL`, and require both API keys exported. Run only against a fresh disposable fixture containing the original ten-unit seed. The test intentionally consumes inventory.

Schema has not changed. `db/init.sql` is a fresh-install initializer, not an upgrade migration. Future schema changes require explicit versioned migrations and backups. Rollout order for this change is Inventory then Orders, and existing clients must adopt the API key header.

NuGet audit:

```sh
dotnet list src/Aether.Orders.Api package --vulnerable --include-transitive
dotnet list src/Aether.Inventory.Api package --vulnerable --include-transitive
```

GitHub Actions builds both APIs, checks vulnerability reports and runs Compose integration tests. Dependabot checks NuGet and workflow dependencies. Enable repository secret scanning/push protection when creating the GitHub repository; no remote repository settings were changed. Never commit `.env`, dumps, logs containing private data or user profiles.

For an existing database, POSTGRES_PASSWORD is an initialization variable, not a password-rotation mechanism. Keep the existing credential until a reviewed database password change has been made, then update application configuration together. Back up valuable data before recreating containers. This update does not change database volumes or run database commands.
