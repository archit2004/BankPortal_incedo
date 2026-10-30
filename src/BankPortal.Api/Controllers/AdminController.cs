using System.Security.Claims;
using BankPortal.Application.Accounts;
using BankPortal.Application.Admin;
using BankPortal.Application.Jobs;
using BankPortal.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BankPortal.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Policy = "AdminOnly")]
public class AdminController(IInterestJobService job, IAdminService admin, TimeProvider clock) : ControllerBase
{
    private string ActorId => User.FindFirstValue("sub") ?? "unknown-admin";

    [HttpGet("ping")]
    public IActionResult Ping() => Ok(new { message = "admin ok" });

    // Manual trigger. Optional ?date=yyyy-MM-dd (not in the future); defaults to today (IST).
    [HttpPost("jobs/run")]
    public async Task<ActionResult<JobRunResult>> RunJob([FromQuery] string? date, CancellationToken ct)
    {
        DateOnly asOf;
        if (string.IsNullOrWhiteSpace(date)) asOf = BankCalendar.Today(clock);
        else if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out asOf))
            throw new DomainException("Date must be in yyyy-MM-dd format.");

        return Ok(await job.RunAsync(asOf, ActorId, ct));
    }

    [HttpGet("customers")]
    public async Task<ActionResult<PagedResult<AdminCustomerDto>>> Customers(
        string? search, int page = 1, int pageSize = 20, CancellationToken ct = default) =>
        Ok(await admin.GetCustomersAsync(ActorId, search, page, pageSize, ct));

    [HttpGet("fixed-deposits")]
    public async Task<ActionResult<PagedResult<AdminFdDto>>> FixedDeposits(
        string? status, int page = 1, int pageSize = 20, CancellationToken ct = default) =>
        Ok(await admin.GetFixedDepositsAsync(ActorId, status, page, pageSize, ct));

    [HttpGet("reports/deposits")]
    public async Task<ActionResult<DepositsReportDto>> DepositsReport(CancellationToken ct) =>
        Ok(await admin.GetDepositsReportAsync(ct));
}