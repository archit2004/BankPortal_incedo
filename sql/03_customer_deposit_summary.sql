/* 03 - Customer-wise deposit summary: joins, aggregations, CTEs, window function.
   TotalDeposits = savings balances + principal of Active FDs (same definition as the admin report API). */
USE BankPortal;
GO

WITH SavingsCte AS (
    SELECT CustomerId, COUNT(*) AS AccountCount, SUM(Balance) AS SavingsBalance
    FROM dbo.Accounts
    GROUP BY CustomerId
),
FdCte AS (
    SELECT a.CustomerId,
           COUNT(*)              AS ActiveFdCount,
           SUM(f.Principal)      AS ActiveFdPrincipal,
           SUM(f.MaturityAmount) AS ActiveFdMaturityValue,
           MIN(f.MaturityDate)   AS NextMaturityDate
    FROM dbo.FixedDeposits f
    JOIN dbo.Accounts a ON a.Id = f.AccountId
    WHERE f.Status = 1
    GROUP BY a.CustomerId
)
SELECT c.Id AS CustomerId, c.FullName, c.Email,
       ISNULL(s.AccountCount, 0)         AS AccountCount,
       ISNULL(s.SavingsBalance, 0)       AS SavingsBalance,
       ISNULL(fd.ActiveFdCount, 0)       AS ActiveFdCount,
       ISNULL(fd.ActiveFdPrincipal, 0)   AS ActiveFdPrincipal,
       ISNULL(fd.ActiveFdMaturityValue, 0) AS ActiveFdMaturityValue,
       fd.NextMaturityDate,
       ISNULL(s.SavingsBalance, 0) + ISNULL(fd.ActiveFdPrincipal, 0) AS TotalDeposits,
       RANK() OVER (ORDER BY ISNULL(s.SavingsBalance, 0) + ISNULL(fd.ActiveFdPrincipal, 0) DESC) AS DepositRank
FROM dbo.Customers c
LEFT JOIN SavingsCte s ON s.CustomerId = c.Id
LEFT JOIN FdCte fd     ON fd.CustomerId = c.Id
ORDER BY TotalDeposits DESC, c.FullName;
GO

-- Bank-wide totals: must equal GET /api/admin/reports/deposits
SELECT (SELECT ISNULL(SUM(Balance), 0)   FROM dbo.Accounts)                              AS SavingsBalance,
       (SELECT ISNULL(SUM(Principal), 0) FROM dbo.FixedDeposits WHERE Status = 1)        AS ActiveFdPrincipal,
       (SELECT ISNULL(SUM(Balance), 0)   FROM dbo.Accounts)
     + (SELECT ISNULL(SUM(Principal), 0) FROM dbo.FixedDeposits WHERE Status = 1)        AS TotalDeposits;
GO