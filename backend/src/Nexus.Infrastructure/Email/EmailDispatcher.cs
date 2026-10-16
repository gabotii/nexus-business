using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexus.Infrastructure.Persistence;

namespace Nexus.Infrastructure.Email;

/// <summary>
/// Delivers the outbox in the background. Rows are claimed with <c>FOR UPDATE SKIP LOCKED</c>, so several API
/// instances never send the same message twice. Recipients are personal data and are not logged.
/// </summary>
internal sealed class EmailDispatcher(
    IServiceScopeFactory scopes,
    IOptions<EmailOptions> options,
    TimeProvider clock,
    ILogger<EmailDispatcher> logger) : BackgroundService
{
    private const int BatchSize = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.DispatcherEnabled)
        {
            logger.LogInformation("E-mail dispatcher disabled by configuration");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.PollIntervalSeconds), clock);
        do
        {
            try
            {
                while (await DispatchBatchAsync(stoppingToken) == BatchSize)
                {
                    // A full batch: keep draining before waiting.
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Database unavailable or similar: try again on the next tick.
                logger.LogError(ex, "E-mail dispatch cycle failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <returns>The number of messages processed (sent or failed).</returns>
    internal async Task<int> DispatchBatchAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var transport = scope.ServiceProvider.GetRequiredService<IEmailTransport>();
        var now = clock.GetUtcNow();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var batch = await db.EmailOutbox
            .FromSqlInterpolated($"""
                SELECT * FROM email_outbox
                WHERE sent_at IS NULL AND failed_at IS NULL AND next_attempt_at <= {now}
                ORDER BY next_attempt_at
                LIMIT {BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);

        foreach (var message in batch)
        {
            try
            {
                await transport.SendAsync(message, ct);
                message.MarkSent(clock.GetUtcNow());
                logger.LogInformation("E-mail {MessageId} delivered", message.Id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.MarkFailed($"{ex.GetType().Name}: {ex.Message}", clock.GetUtcNow());
                logger.LogWarning(ex, "E-mail {MessageId} failed (attempt {Attempt})", message.Id, message.Attempts);
            }
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return batch.Count;
    }
}

/// <summary>Hourly clean-up of rows that only matter for a while: old sessions and delivered e-mails.</summary>
internal sealed class HousekeepingService(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<HousekeepingService> logger) : BackgroundService
{
    internal static readonly TimeSpan SessionRetention = TimeSpan.FromDays(7);
    internal static readonly TimeSpan EmailRetention = TimeSpan.FromDays(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), clock);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Housekeeping failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = clock.GetUtcNow();
        var sessionCutoff = now - SessionRetention;
        var emailCutoff = now - EmailRetention;

        var sessions = await db.UserSessions
            .Where(s => s.ExpiresAt < sessionCutoff || s.RevokedAt < sessionCutoff)
            .ExecuteDeleteAsync(ct);
        var emails = await db.EmailOutbox
            .Where(m => m.SentAt < emailCutoff || m.FailedAt < emailCutoff)
            .ExecuteDeleteAsync(ct);

        if (sessions + emails > 0)
        {
            logger.LogInformation("Housekeeping removed {Sessions} sessions and {Emails} e-mails", sessions, emails);
        }
    }
}
