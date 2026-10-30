using BankPortal.Application.Jobs;
using BankPortal.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BankPortal.Infrastructure.Jobs;

public class JobOptions
{
    public bool Enabled { get; set; } = true;
    public bool RunOnStartup { get; set; } = true;   // catch-up run shortly after the app starts
    public string RunAtIst { get; set; } = "01:00";  // daily run time, India Standard Time
}

public class InterestBackgroundService(
    IServiceScopeFactory scopes, TimeProvider clock,
    IOptions<JobOptions> options, ILogger<InterestBackgroundService> log) : BackgroundService
{
    private readonly JobOptions _opt = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_opt.Enabled)
        {
            log.LogInformation("Interest job scheduler is disabled (Jobs:Enabled=false).");
            return;
        }

        var runAt = TimeOnly.TryParse(_opt.RunAtIst, out var t) ? t : new TimeOnly(1, 0);

        try
        {
            if (_opt.RunOnStartup)
            {
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
                await RunOnceAsync(stoppingToken);
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(UntilNextRun(runAt), stoppingToken);
                await RunOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private TimeSpan UntilNextRun(TimeOnly runAt)
    {
        var nowIst = clock.GetUtcNow().UtcDateTime.AddMinutes(330);
        var next = nowIst.Date + runAt.ToTimeSpan();
        if (next <= nowIst) next = next.AddDays(1);
        return next - nowIst;
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var job = scope.ServiceProvider.GetRequiredService<IInterestJobService>();
            await job.RunAsync(BankCalendar.Today(clock), JobActors.Scheduler, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Scheduled interest job failed; it will retry at the next run.");
        }
    }
}