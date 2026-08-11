using CMPlus.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CMPlus.Infrastructure.Idempotency;

/// <summary>
/// S13-DB-01 DoD: "งานล้างข้อมูลเก่าอัตโนมัติพร้อมนโยบายเก็บรักษาที่บันทึกไว้" (an automatic cleanup
/// job with a documented retention policy - see <see cref="IdempotencyOptions"/>). Runs the sweep
/// once immediately on startup (reclaims any backlog from before the process last ran, e.g. after a
/// redeploy) and then on <see cref="IdempotencyOptions.CleanupInterval"/>.
///
/// <para>Creates a fresh DI scope per sweep rather than depending on a long-lived
/// <see cref="Application.Abstractions.IIdempotencyKeyMaintenance"/>/<c>CmPlusDbContext</c> instance
/// - this type itself is registered as a singleton (the normal <see cref="BackgroundService"/>
/// lifetime), but <c>CmPlusDbContext</c> is Scoped, so it cannot be constructor-injected directly
/// here.</para>
/// </summary>
public sealed class IdempotencyKeyCleanupService(
    IServiceScopeFactory scopeFactory,
    IOptions<IdempotencyOptions> options,
    IDateTimeProvider clock,
    ILogger<IdempotencyKeyCleanupService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.CleanupInterval);

        do
        {
            try
            {
                await PurgeOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A failed sweep is never fatal to the API - the next tick tries again, and nothing
                // request-time depends on this job having run recently (retention is a housekeeping
                // concern, not a correctness one - see IdempotencyOptions' remarks on why the TTL is
                // generous rather than tight).
                logger.LogError(ex, "IdempotencyKey cleanup sweep failed; will retry on the next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task PurgeOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var maintenance = scope.ServiceProvider.GetRequiredService<IIdempotencyKeyMaintenance>();

        var result = await maintenance.PurgeExpiredAsync(clock.UtcNow, cancellationToken);

        if (result.TotalRowsDeleted > 0)
        {
            logger.LogInformation(
                "IdempotencyKey cleanup: removed {CompletedRows} expired completed row(s) and {StaleRows} abandoned in-progress row(s).",
                result.CompletedRowsDeleted,
                result.StaleInProgressRowsDeleted);
        }
    }
}
