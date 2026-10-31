# Demo script (about 15 minutes)

Setup: `docker compose up -d --build`, open http://localhost:8080. The admin login is `ADMIN_EMAIL` / `ADMIN_PASSWORD` in `.env`.
For the SQL steps, connect to `localhost,14330` as `sa` with `MSSQL_SA_PASSWORD` from `.env` (trust the certificate).

## 1. Customer basics (3 min)
1. Register `demo@example.com` with a strong password. You land on a zero balance.
2. Deposit 200,000. The balance and transaction list update without a reload.
3. Try an overdraft (withdraw 999,999,999): "Insufficient funds". Try `10.123`: rejected on the form.
Say: money is `decimal`, the ledger is append-only, and every change writes a transaction row in the same database transaction.

## 2. Fixed deposit (3 min)
1. Open **Fixed deposits**. Enter 100,000 for 12 months: 7.00% and an estimated maturity of 107,185.90. Tenure 3 shows "No interest rate is available".
2. Open it. The savings balance drops by 100,000 and the ledger shows an `FdOpen` row.
Say: the rate comes from the rate-card table, never from the browser; maturity is `P x (1 + r/4)^(4 x years)`.

## 3. Safety under retries and concurrency (2 min)
- Show `AccountTests.TwentyParallelWithdrawals_...`: 20 parallel withdrawals against 1,000 give exactly 10 successes and a final balance of 0.
- Say: row versions plus retry, an idempotency key on every money operation, and a database CHECK constraint as the last line of defence.

## 4. Premature closure (2 min)
Age the FD by 200 days (test-only edit):
```sql
UPDATE BankPortal.dbo.FixedDeposits SET StartDate = DATEADD(day, -200, StartDate) WHERE Status = 1 AND Principal = 100000;
```
1. Reload the FD page. The quote shows 5.00% (6.00% tier minus 1% penalty), interest 2,760.15, penalty 556.94, payout 102,760.15.
2. Close it (confirm). The balance rises by the payout; a second close is refused.

## 5. Interest job and maturity (2 min)
1. Open a new FD of 50,000 for 12 months. Age both dates by 365 days (test-only edit):
```sql
UPDATE BankPortal.dbo.FixedDeposits
SET StartDate = DATEADD(day, -365, StartDate), MaturityDate = DATEADD(day, -365, MaturityDate)
WHERE Status = 1 AND Principal = 50000;
```
2. Sign in as admin, open **Interest job**, click **Run job**: 365 accruals, 1 matured, 53,592.95 paid out. Click again: zero new accruals.
3. Sign back in as the customer: the FD is `Matured` and the balance includes the payout.
Say: the scheduler runs this nightly; the unique index and the payout key make it safe to re-run.

## 6. Admin and SQL (3 min)
1. Admin tabs: deposits report, customers (search, paging), FDs (status filter).
2. Run the three files in `sql/`. The totals query in `03_customer_deposit_summary.sql` matches the report.
3. Show the audit trail:
```sql
SELECT TOP 10 Action, Entity, ActorId, Details, CreatedAt FROM BankPortal.dbo.AuditLog ORDER BY Id DESC;
```

## Close with
113 automated tests, CI that builds and smoke-tests the Docker stack, and the known gaps in `docs/architecture.md` (password reset, refresh-token storage, TLS, job locking).