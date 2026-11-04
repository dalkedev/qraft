using Microsoft.EntityFrameworkCore;
using Qraft.Contracts;
using Qraft.Messaging;
using Qraft.Simulator.Data;

namespace Qraft.Simulator;

public sealed class SubmissionOutboxWorker(IDbContextFactory<SimulatorDbContext> factory,
    RabbitMqBus bus, ILogger<SubmissionOutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var database = await factory.CreateDbContextAsync(stoppingToken);
                var now = DateTime.UtcNow;
                var due = await database.Outbox.AsNoTracking()
                    .Where(item => item.PublishedAt == null && item.NextAttemptAt <= now)
                    .OrderBy(item => item.CreatedAt).Take(20).Select(item => item.Id).ToListAsync(stoppingToken);
                foreach (var id in due) await PublishOneAsync(id, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Simulator outbox temporarily unavailable; retrying");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task PublishOneAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var database = await factory.CreateDbContextAsync(cancellationToken);
        var entry = await database.Outbox.SingleAsync(item => item.Id == id, cancellationToken);
        if (entry.PublishedAt.HasValue) return;
        var source = await database.Messages.AsNoTracking()
            .SingleAsync(item => item.MessageId == entry.SourceMessageId, cancellationToken);
        entry.Attempts++;
        try
        {
            await bus.PublishAsync(QueueNames.Submissions, source.PayloadJson, source.MessageId.ToString(),
                source.RunId.ToString(), cancellationToken);
            // A crash after the broker confirm and before this save can resend the same MessageId.
            entry.PublishedAt = DateTime.UtcNow;
            entry.LastError = null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            var delay = Math.Min(30d, Math.Pow(2d, Math.Min(entry.Attempts - 1, 5)));
            entry.NextAttemptAt = DateTime.UtcNow.AddSeconds(delay);
            entry.LastError = exception.Message.Length <= 1500 ? exception.Message : exception.Message[..1500];
            logger.LogWarning("Simulator message {MessageId} publish attempt {Attempt} failed; retry in {Delay}s: {Reason}",
                source.MessageId, entry.Attempts, delay, entry.LastError);
        }
        await database.SaveChangesAsync(cancellationToken);
    }
}
