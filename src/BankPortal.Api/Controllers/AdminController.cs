using System.Security.Claims;
using BankPortal.Application.Jobs;
using BankPortal.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BankPortal.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Policy = "AdminOnly")]
public class AdminController(IInterestJobService job, TimeProvider clock) : ControllerBase
{
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

        var actor = User.FindFirstValue("sub") ?? "unknown-admin";
        return Ok(await job.RunAsync(asOf, actor, ct));
    }
}