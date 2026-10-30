using System.Security.Claims;
using BankPortal.Application.Accounts;
using BankPortal.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BankPortal.Api.Controllers;

[ApiController]
[Route("api/accounts")]
[Authorize(Roles = Roles.Customer)]
public class AccountsController(IAccountService accounts) : ControllerBase
{
    private string UserId => User.FindFirstValue("sub")
        ?? throw new AuthException("Invalid token.", 401);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AccountDto>>> List(CancellationToken ct) =>
        Ok(await accounts.GetAccountsAsync(UserId, ct));

    [HttpGet("{accountId:guid}/dashboard")]
    public async Task<ActionResult<DashboardDto>> Dashboard(Guid accountId, CancellationToken ct) =>
        Ok(await accounts.GetDashboardAsync(UserId, accountId, ct));

    [HttpGet("{accountId:guid}/transactions")]
    public async Task<ActionResult<PagedResult<TransactionDto>>> Transactions(
        Guid accountId, int page = 1, int pageSize = 20, CancellationToken ct = default) =>
        Ok(await accounts.GetTransactionsAsync(UserId, accountId, page, pageSize, ct));

    [HttpPost("{accountId:guid}/deposit")]
    public async Task<ActionResult<TransactionDto>> Deposit(
        Guid accountId, MoneyRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken ct) =>
        Ok(await accounts.DepositAsync(UserId, accountId, request.Amount, request.Reference, idempotencyKey ?? "", ct));

    [HttpPost("{accountId:guid}/withdraw")]
    public async Task<ActionResult<TransactionDto>> Withdraw(
        Guid accountId, MoneyRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken ct) =>
        Ok(await accounts.WithdrawAsync(UserId, accountId, request.Amount, request.Reference, idempotencyKey ?? "", ct));
}