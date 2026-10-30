# SQL deliverables

| File | Purpose |
|---|---|
| 01_fd_maturity_value.sql | Maturity value calculator, and recomputation of every stored FD |
| 02_interest_accrual.sql | Accrued vs remaining interest, daily postings, gap check |
| 03_customer_deposit_summary.sql | Customer-wise deposits (CTEs, joins, aggregates, RANK) and bank totals |

Run each file in the VS Code SQL extension, SSMS or Azure Data Studio, connected to the BankPortal database.

## Indexes these queries rely on (all created by the EF migrations)
- IX_Accounts_CustomerId: customer to accounts join
- IX_FixedDeposits_AccountId and IX_FixedDeposits_Status_MaturityDate: FD to account join and Active-FD filters
- IX_InterestAccruals_FixedDepositId_AccrualDate (unique): per-FD accrual aggregation and the job's idempotency
- Optional later: add INCLUDE (Amount) to the accrual index so SUM(Amount) is a covering seek, which needs a new migration