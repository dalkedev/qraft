using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Qraft.Application;
using Qraft.Contracts;
using Qraft.Infrastructure;
using Qraft.Messaging;

namespace Qraft.Worker;

public sealed class InspectionConsumer(IServiceScopeFactory scopes, RabbitMqBus bus,
    ILogger<InspectionConsumer> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        bus.ConsumeAsync(QueueNames.Submissions, HandleAsync, stoppingToken);

    private async Task HandleAsync(BrokerDelivery delivery, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(delivery.MessageId, out var messageId) || messageId == Guid.Empty)
            throw new RejectDeliveryException("Missing or invalid AMQP MessageId.");
        if (delivery.Body.Length > 128 * 1024)
            throw new RejectDeliveryException("Payload exceeds 128 KiB.");

        string rawJson;
        try { rawJson = new UTF8Encoding(false, true).GetString(delivery.Body); }
        catch (DecoderFallbackException) { throw new RejectDeliveryException("Payload is not valid UTF-8."); }
        var payloadHash = Convert.ToHexString(SHA256.HashData(delivery.Body));
        using var scope = scopes.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IQualityStore>();
        var existing = await store.FindAsync(messageId, cancellationToken);
        if (existing is not null)
        {
            if (existing.PayloadHash != payloadHash)
                throw new RejectDeliveryException("An existing MessageId was reused with a different payload.");
            await bus.PublishAsync(QueueNames.Results, MessageJson.Serialize(existing.Result),
                existing.Result.ResultId.ToString(), messageId.ToString(), cancellationToken);
            logger.LogInformation("Replayed saved result for {MessageId}", messageId);
            return;
        }

        var processor = scope.ServiceProvider.GetRequiredService<InspectionProcessor>();
        var now = DateTimeOffset.UtcNow;
        InspectionSubmittedV1? submission = null;
        InspectionProcessedV1 result;
        try
        {
            submission = MessageJson.Deserialize<InspectionSubmittedV1>(rawJson);
            if (submission is null)
                result = processor.Reject(messageId, now, "invalid_json", "$", "Komunikat nie zawiera obiektu JSON.");
            else if (submission.MessageId != messageId)
                result = processor.Reject(messageId, now, "message_id_mismatch", "messageId", "Identyfikator AMQP nie zgadza się z treścią.");
            else if (!Guid.TryParse(delivery.CorrelationId, out var simulationId) || simulationId != submission.SimulationId)
                result = processor.Reject(messageId, now, "correlation_mismatch", "simulationId", "Identyfikator symulacji nie zgadza się z korelacją AMQP.");
            else result = processor.Process(submission, now);
        }
        catch (JsonException)
        {
            result = processor.Reject(messageId, now, "invalid_json", "$", "Nieprawidłowy JSON albo typ lub nazwa pola.");
        }

        if (result.SimulationId == Guid.Empty && Guid.TryParse(delivery.CorrelationId, out var sourceSimulationId))
            result = result with { SimulationId = sourceSimulationId };

        try { await store.SaveAsync(submission, result, rawJson, payloadHash, cancellationToken); }
        catch (MessageIdentityConflictException exception) { throw new RejectDeliveryException(exception.Message); }
        logger.LogInformation("Saved {MessageId}: {Status}, quality {Outcome}", messageId, result.Status, result.Outcome ?? "n/a");
    }
}
