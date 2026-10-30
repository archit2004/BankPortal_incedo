/* 01 - FD maturity value calculation
   Formula: M = P * (1 + r/n) ^ (n * months/12), r as a fraction, n = compounding periods per year.
   Notes: SQL ROUND is half-away-from-zero while the app uses banker's rounding, so a difference
   of 0.01 can appear at exact midpoints. FDs whose dates were edited by hand for testing show
   date mismatches; that comes from the edit, not the formula. */
USE BankPortal;
GO

-- 1A. Ad-hoc calculator: change the four values
DECLARE @Principal decimal(19,4) = 100000,
        @RatePercent decimal(7,4) = 7,
        @TenureMonths int = 12,
        @CompoundingPerYear int = 4;

SELECT @Principal AS Principal,
       CAST(ROUND(@Principal * POWER(1 + CAST(@RatePercent AS float) / 100 / @CompoundingPerYear,
                                     @CompoundingPerYear * @TenureMonths / 12.0), 2) AS decimal(19,2)) AS MaturityAmount;
GO

-- 1B. Recompute every FD and compare with what the application stored
SELECT f.Id AS FdId, c.FullName, a.AccountNumber, f.Status,
       f.Principal, f.AnnualRatePercent, f.TenureMonths, f.CompoundingPerYear,
       f.StartDate,
       f.MaturityDate                                   AS StoredMaturityDate,
       DATEADD(month, f.TenureMonths, f.StartDate)      AS ComputedMaturityDate,
       f.MaturityAmount                                 AS StoredMaturityAmount,
       calc.ComputedMaturityAmount,
       f.MaturityAmount - calc.ComputedMaturityAmount   AS Difference
FROM dbo.FixedDeposits f
JOIN dbo.Accounts  a ON a.Id = f.AccountId
JOIN dbo.Customers c ON c.Id = a.CustomerId
CROSS APPLY (
    SELECT CAST(ROUND(f.Principal * POWER(1 + CAST(f.AnnualRatePercent AS float) / 100 / f.CompoundingPerYear,
                                          f.CompoundingPerYear * f.TenureMonths / 12.0), 2) AS decimal(19,2)) AS ComputedMaturityAmount
) calc
ORDER BY f.StartDate DESC;
GO