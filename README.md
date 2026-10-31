# BankPortal: Digital Savings and Fixed Deposit Portal

ASP.NET Core 9 Web API, EF Core, SQL Server and a React + TypeScript UI. Customers manage a savings account and open fixed deposits;
admins see customers, deposits and run the interest job.

Design, rules and known gaps: [docs/architecture.md](docs/architecture.md). Walkthrough: [docs/demo-script.md](docs/demo-script.md).

## Run everything with Docker

```powershell
# one time: create .env from the template and fill in the values (or let the setup script generate it)
copy .env.example .env
docker compose up -d --build
```

Open http://localhost:8080 and sign in as `ADMIN_EMAIL` / `ADMIN_PASSWORD` from `.env`. Customers register through the UI.
`docker compose down` keeps the data; `docker compose down -v` wipes it.

## Develop locally

1. Start SQL Server (Docker) and set the connection string and JWT key as user secrets on `src/BankPortal.Api`
   (`ConnectionStrings:Default`, `Jwt:Key`, `Seed:AdminEmail`, `Seed:AdminPassword`).
2. `dotnet ef database update --project src/BankPortal.Infrastructure --startup-project src/BankPortal.Api`
3. `dotnet run --project src/BankPortal.Api` (http://localhost:5180)
4. `cd web; npm install; npm run dev` (http://localhost:5173, proxies /api to the API)

## Tests

```powershell
dotnet test    # 60 unit tests + 53 integration tests (needs the SQL Server container)
```

Integration tests create and drop `BankPortal_IntegrationTests` on the same server and never touch the development database.
In CI they use `BANKPORTAL_IT_CONNECTION`.

## SQL deliverables

`sql/` holds the FD maturity value, interest accrual and customer-wise deposit summary queries. See `sql/README.md`.