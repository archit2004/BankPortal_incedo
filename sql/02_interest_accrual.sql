/* 02 - Interest accrual queries (IST date, same convention as the app) */
USE BankPortal;
GO

-- 2A. Accrued interest per FD up to a date, and what is still to come
DECLARE @AsOf date = CAST(DATEADD(minute, 330, SYSUTCDATETIME()) AS date);

SELECT f.Id AS FdId, c.FullName, f.Status, f.Principal,
       f.MaturityAmount - f.Principal                         AS TotalInterestAtMaturity,
       ISNULL(x.AccruedToDate, 0)                             AS AccruedToDate,
       ISNULL(x.DaysAccrued, 0)                               AS DaysAccrued,
       x.LastAccrualDate,
       (f.MaturityAmount - f.Principal) - ISNULL(x.AccruedToDate, 0) AS RemainingInterest
FROM dbo.FixedDeposits f
JOIN dbo.Accounts  a ON a.Id = f.AccountId
JOIN dbo.Customers c ON c.Id = a.CustomerId
LEFT JOIN (
    SELECT FixedDepositId, SUM(Amount) AS AccruedToDate, COUNT(*) AS DaysAccrued, MAX(AccrualDate) AS LastAccrualDate
    FROM dbo.InterestAccruals
    WHERE AccrualDate <= @AsOf
    GROUP BY FixedDepositId
) x ON x.FixedDepositId = f.Id
WHERE f.Status IN (1, 2)   -- Active, Matured
ORDER BY f.MaturityDate;
GO

-- 2B. Interest posted on one date (the job's daily output)
DECLARE @AccrualDate date = CAST(DATEADD(minute, 330, SYSUTCDATETIME()) AS date);

SELECT ia.AccrualDate, COUNT(*) AS FdsAccrued, SUM(ia.Amount) AS InterestPosted
FROM dbo.InterestAccruals ia
WHERE ia.AccrualDate = @AccrualDate
GROUP BY ia.AccrualDate;
GO

-- 2C. Gap check: Active FDs whose accrual rows are fewer than the days elapsed (the job heals these on its next run)
DECLARE @AsOf date = CAST(DATEADD(minute, 330, SYSUTCDATETIME()) AS date);

SELECT f.Id AS FdId, f.StartDate, f.MaturityDate,
       CASE WHEN @AsOf >= f.MaturityDate THEN DATEDIFF(day, f.StartDate, f.MaturityDate)
            ELSE DATEDIFF(day, f.StartDate, @AsOf) END AS ExpectedRows,
       COUNT(ia.Id) AS ActualRows
FROM dbo.FixedDeposits f
LEFT JOIN dbo.InterestAccruals ia ON ia.FixedDepositId = f.Id
WHERE f.Status = 1
GROUP BY f.Id, f.StartDate, f.MaturityDate
HAVING COUNT(ia.Id) <> CASE WHEN @AsOf >= f.MaturityDate THEN DATEDIFF(day, f.StartDate, f.MaturityDate)
                            ELSE DATEDIFF(day, f.StartDate, @AsOf) END;
GO