# Architecture note

## 1. Scope

A basic digital banking portal. Customers log in, see a savings account, deposit and withdraw, open fixed deposits (FDs) and close them early.
A scheduled job accrues daily interest and credits matured FDs. Admins see all customers, all FDs and a total deposits report.

## 2. Structure

- `BankPortal.Domain`: entities, Money, FdCalculator, InterestSchedule, RateLookup. No dependencies.
- `BankPortal.Application`: DTOs, service interfaces, request validation attributes.
- `BankPortal.Infrastructure`: EF Core DbContext and migrations, services, Identity, JWT, interest job.
- `BankPortal.Api`: controllers, middleware, health checks, DI, rate limiting.
- `web/`: React + TypeScript (Vite, React Query), served by nginx in Docker.
- `sql/`: the three SQL deliverables.
Dependencies point inward. Business rules that need no I/O (money math, FD maturity and penalty, rate lookup, accrual schedule) live in Domain
and are unit tested without a database.

## 3. Money rules

- All amounts are `decimal` in C# and `decimal(19,4)` in SQL. Interest rates are `decimal(7,4)`.
- Rounding is banker's rounding (`MidpointRounding.ToEven`) to 2 places, applied once when a `Money` value is created, never on intermediate values.
- The only use of `double` is `Math.Pow` for fractional exponents (no decimal equivalent). Its result is converted back and rounded once.
  SQL's `ROUND` rounds half away from zero, so reconciliation queries can differ from the app by 0.01 at exact midpoints.
- Currency is carried on `Money`; mixing currencies throws.

## 4. Fixed deposit rules (all defaults are constants in one place)

- Rate comes from the `RateCard` table by tenure and effective date. The client never sends a rate. Each FD keeps its contracted rate.
- Maturity amount: `P x (1 + r/n)^(n x months/12)`, quarterly compounding (n = 4) by default. Maturity date: start date plus the tenure in months (month-end clamped).
- Premature closure: the rate is the rate-card tier for the completed months, capped at the contracted rate, minus a 1% penalty (floored at 0%).
  Below the card's 6-month minimum the fallback rate is 3.00% (`RateLookup.BelowMinimumRatePercent`). Interest accrues for actual days / 365.
  Closing within 7 days pays no interest. The penalty shown is the interest lost to the 1% reduction.
- Closing on or after maturity pays the full maturity amount with no penalty and marks the FD `Matured`.
- "Today" is the IST date (fixed UTC+5:30) through `TimeProvider`, so tests can control the clock.

## 5. Consistency and correctness

- **Ledger:** `Account.Balance` has a private setter. It changes only through `Deposit`, `Withdraw` and `Apply`, and each writes a `Transactions` row
  with the running balance in the same `SaveChanges` (one database transaction). Transactions are never updated or deleted; deletes are restricted by foreign keys.
- **Overdraft protection, three layers:** the domain check, optimistic concurrency, and a database `CHECK (Balance >= 0)`.
- **Optimistic concurrency:** `Accounts` and `FixedDeposits` carry a SQL `rowversion`. A conflict reloads state and re-evaluates, up to 3 attempts, then returns 409.
  Unique-key violations and deadlock victims (errors 2601, 2627, 1205) are retried the same way.
- **Idempotency:** deposit, withdraw, FD open and FD close require an `Idempotency-Key`. A filtered unique index on `(AccountId, IdempotencyKey)` backs it.
  A replay returns the original result and moves no money. Reusing a key with a different request returns 409. FD operations tie a key to its FD through the
  ledger `Reference` (`FD <id>`), so no extra table is needed.
- **Proven by tests:** 20 parallel withdrawals against a balance of 1,000 give exactly 10 successes and a final balance of 0; 10 parallel requests with one key move money once;
  5 parallel FD opens competing for a balance never spend more than it.

## 6. Interest job

- `InterestJobService` accrues one `InterestAccrual` per active FD per day. The unique `(FixedDepositId, AccrualDate)` index makes re-runs and concurrent runs safe.
- Accrual is compound-consistent: day d's accrual is the difference of rounded cumulative values, so a full tenure sums to exactly `MaturityAmount - Principal`.
- Each run backfills every missing day, so a missed night heals itself. Each FD is processed in its own transaction; one failure does not stop the batch.
- On the maturity date it credits the maturity amount (ledger key `maturity-<fdId>`), marks the FD `Matured` and writes an `FD_MATURED` audit row.
  A customer closing at maturity first is handled: the job then only completes accrual rows.
- `InterestBackgroundService` runs daily at a configurable IST time (default 01:00) and once shortly after start-up. Admins can trigger a run for any past date
  (`POST /api/admin/jobs/run`), which writes a `JOB_RUN` audit row.

## 7. Security

- ASP.NET Core Identity: hashed passwords, 10+ character policy, lockout after 5 failures for 15 minutes. Login errors do not reveal whether the email exists.
- JWT access tokens (15 minutes). Refresh tokens (7 days) are random, stored only as SHA-256 hashes, and rotate on use. Replaying a rotated token revokes all of that user's tokens.
- Roles: `Customer` and `Admin`. Customer endpoints reject admins and the reverse. Every customer query filters by the caller's user id, and other customers' records return 404 (no existence leak).
- Auth endpoints are rate limited per client IP. Behind nginx, `X-Forwarded-For` is trusted only when `ForwardedHeaders:Enabled` is set, and nginx overwrites the header.
- Secrets: user secrets locally, a gitignored `.env` for Compose, environment variables in deployment. Nothing secret is in source control.
- nginx adds security headers and a CSP. The API container publishes no port; only nginx reaches it.
- Admin reads of customer and FD lists are audited (`VIEW_*`). The deposits report returns only aggregates.

## 8. Operations

- Structured JSON console logs with a per-request correlation id (`X-Correlation-Id`, also in error bodies). Expected business errors log at Information or Warning
  without stack traces; only unexpected failures log at Error. EF's per-command SQL logging is set to Warning.
- Health endpoints: `/health/live` (no dependencies) and `/health/ready` (checks SQL).
- Docker Compose runs SQL Server, a one-shot migrator (an EF migrations bundle), the API and nginx. The API starts only after the migrator exits successfully.
  The API's runtime account never needs schema rights, and instances cannot race a migration.
- CI (GitHub Actions) builds, runs unit and integration tests against SQL Server, builds the frontend and smoke-tests the Docker stack.
- Deviation from the original plan: logging uses the built-in JSON console logger, not Serilog.

## 9. Testing

60 unit tests (money, FD maths, rate lookup, accrual schedule, account rules) and 53 integration tests over the real HTTP pipeline and SQL Server
(auth, savings, FDs, interest job, admin, concurrency, health, correlation ids). The frontend has no automated tests yet.

## 10. Known gaps

- No password change or reset, no email verification, no MFA.
- The refresh token is in `sessionStorage` (readable by an XSS bug). An HttpOnly, SameSite cookie is the stronger option.
- Compose serves plain HTTP; TLS must terminate upstream. The rate limiter's counters are in memory per instance.
- The job has no distributed lock. Several API instances would all run it: correct (idempotent) but wasteful. Run it in one instance or a separate worker.
- Interest accrued before an early closure is not reversed by ledger rows; the payout uses the penalty formula instead, and reports count accruals only for Active and Matured FDs.
- Customers have one savings account in practice (the API supports several). No transfers, statements or notifications.
- Admin totals are computed live per request, which is fine for this size.
- The 3% below-minimum rate and the 1% penalty are defaults I chose; confirm them against the business rules.

## 11. Scaling path

Move the job to a worker with a lease or lock; add an indexed view or read model for admin totals; keyset paging for the ledger; a distributed cache for rate limiting;
a managed secret store; read replicas for reporting; and an outbox for notifications.