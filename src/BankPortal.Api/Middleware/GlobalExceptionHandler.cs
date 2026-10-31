using BankPortal.Application.Accounts;
using BankPortal.Application.Auth;
using BankPortal.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BankPortal.Api.Middleware;

public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        var (status, title) = ex switch
        {
            AuthException a => (a.StatusCode, a.Message),
            NotFoundException n => (StatusCodes.Status404NotFound, n.Message),
            ConflictException c => (StatusCodes.Status409Conflict, c.Message),
            DomainException d => (StatusCodes.Status400BadRequest, d.Message),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict,
                "The record was changed by another request. Please retry."),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.")
        };

        // Only unexpected failures are errors. Expected business outcomes must not trip error alerts
        // or bury real problems. Failed logins are warnings because they matter for security monitoring.
        if (status >= 500)
            logger.LogError(ex, "Unhandled exception on {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
        else if (status is 401 or 423 or 429)
            logger.LogWarning("{Method} {Path} -> {Status}: {Title}", ctx.Request.Method, ctx.Request.Path, status, title);
        else
            logger.LogInformation("{Method} {Path} -> {Status}: {Title}", ctx.Request.Method, ctx.Request.Path, status, title);

        ctx.Response.StatusCode = status;
        await ctx.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = title,
            Extensions = { ["correlationId"] = ctx.TraceIdentifier }
        }, ct);
        return true;
    }
}