using System.ComponentModel.DataAnnotations;

namespace BankPortal.Application.FixedDeposits;

public record RateDto(int MinTenureMonths, int MaxTenureMonths, decimal AnnualRatePercent, DateOnly EffectiveFrom);

public record FdDto(
    Guid Id, Guid AccountId, decimal Principal, decimal AnnualRatePercent, int TenureMonths,
    int CompoundingPerYear, DateOnly StartDate, DateOnly MaturityDate, decimal MaturityAmount,
    string Status, DateOnly? ClosedOn, decimal? PenaltyApplied);

public record PrematureQuoteDto(
    decimal Payout, decimal Interest, decimal Penalty, decimal EffectiveRatePercent,
    int DaysHeld, bool InterestForfeited, DateOnly AsOf);

public record FdDetailDto(FdDto Fd, PrematureQuoteDto? CloseQuote);

public record CloseResultDto(
    FdDto Fd, decimal Payout, decimal Interest, decimal Penalty, Guid TransactionId, decimal BalanceAfter);

public class OpenFdRequest
{
    [Range(1000.0, 100000000.0)] public decimal Amount { get; set; }
    [Range(1, 120)] public int TenureMonths { get; set; }
}

public interface IFixedDepositService
{
    Task<IReadOnlyList<RateDto>> GetRatesAsync(CancellationToken ct);
    Task<IReadOnlyList<FdDto>> ListAsync(string userId, CancellationToken ct);
    Task<FdDetailDto> GetAsync(string userId, Guid fdId, CancellationToken ct);
    Task<FdDto> OpenAsync(string userId, Guid accountId, decimal amount, int tenureMonths, string idempotencyKey, CancellationToken ct);
    Task<CloseResultDto> CloseAsync(string userId, Guid fdId, string idempotencyKey, CancellationToken ct);
}