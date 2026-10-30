using System.Security.Claims;
using BankPortal.Application.Auth;
using BankPortal.Application.FixedDeposits;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BankPortal.Api.Controllers;

[ApiController]
[Authorize(Roles = Roles.Customer)]
public class FixedDepositsController(IFixedDepositService fds) : ControllerBase
{
    private string UserId => User.FindFirstValue("sub")
        ?? throw new AuthException("Invalid token.", 401);

    [HttpGet("api/fixed-deposits/rates")]
    public async Task<ActionResult<IReadOnlyList<RateDto>>> Rates(CancellationToken ct) =>
        Ok(await fds.GetRatesAsync(ct));

    [HttpGet("api/fixed-deposits")]
    public async Task<ActionResult<IReadOnlyList<FdDto>>> List(CancellationToken ct) =>
        Ok(await fds.ListAsync(UserId, ct));

    [HttpGet("api/fixed-deposits/{id:guid}")]
    public async Task<ActionResult<FdDetailDto>> Get(Guid id, CancellationToken ct) =>
        Ok(await fds.GetAsync(UserId, id, ct));

    [HttpPost("api/accounts/{accountId:guid}/fixed-deposits")]
    public async Task<ActionResult<FdDto>> Open(
        Guid accountId, OpenFdRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken ct) =>
        Ok(await fds.OpenAsync(UserId, accountId, request.Amount, request.TenureMonths, idempotencyKey ?? "", ct));

    [HttpPost("api/fixed-deposits/{id:guid}/close")]
    public async Task<ActionResult<CloseResultDto>> Close(
        Guid id, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken ct) =>
        Ok(await fds.CloseAsync(UserId, id, idempotencyKey ?? "", ct));
}