using Microsoft.EntityFrameworkCore;
using Qraft.Contracts;
using Qraft.Infrastructure;
using Qraft.Messaging;

namespace Qraft.Worker;

public sealed class ResultOutboxPublisher(IServiceScopeFactory scopes, RabbitMqBus bus,
    ILogger<ResultOutboxPublisher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var database = scope.ServiceProvider.GetRequiredService<QualityDbContext>();
                var now = DateTime.UtcNow;
                var messages = await database.ResultOutbox
                    .Where(x => x.PublishedAtUtc == null && x.NextAttemptAtUtc <= now)
                    .OrderBy(x => x.CreatedAtUtc).Take(20).ToListAsync(stoppingToken);
                foreach (var message in messages)
                {
                    try
                    {
                        await bus.PublishAsync(QueueNames.Results, message.PayloadJson, message.Id.ToString(),
                            message.SourceMessageId.ToString(), stoppingToken);
                        message.PublishedAtUtc = DateTime.UtcNow;
                        message.LastError = null;
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                    catch (Exception exception)
                    {
                        message.LastError = exception.GetType().Name;
                        logger.LogWarning("Result {MessageId} awaits broker: {Error}", message.SourceMessageId, message.LastError);
                        message.NextAttemptAtUtc = DateTime.UtcNow.AddSeconds(Math.Min(30, Math.Pow(2, Math.Min(message.Attempts, 5))));
                    }
                    message.Attempts++;
                    await database.SaveChangesAsync(stoppingToken);
                }
                await Task.Delay(500, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Outbox database unavailable; retrying");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }
}
