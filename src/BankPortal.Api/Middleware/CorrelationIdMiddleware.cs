namespace BankPortal.Api.Middleware;

// Gives every request an id, returns it in X-Correlation-Id and puts it on every log line for the request.
public class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string Header = "X-Correlation-Id";

    public async Task Invoke(HttpContext ctx)
    {
        var id = ctx.Request.Headers[Header].FirstOrDefault();
        // Accept only short, simple ids so clients cannot inject odd content into logs or headers.
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64 || !id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            id = Guid.NewGuid().ToString("N");

        ctx.TraceIdentifier = id;
        ctx.Response.OnStarting(() =>
        {
            ctx.Response.Headers[Header] = id;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = id }))
            await next(ctx);
    }
}