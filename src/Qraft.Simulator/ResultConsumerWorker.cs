using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Qraft.Contracts;
using Qraft.Messaging;
using Qraft.Simulator.Data;

namespace Qraft.Simulator;

public sealed class ResultConsumerWorker(IDbContextFactory<SimulatorDbContext> factory,
    RabbitMqBus bus, ILogger<ResultConsumerWorker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        bus.ConsumeAsync(QueueNames.Results, HandleAsync, stoppingToken);

    private async Task HandleAsync(BrokerDelivery delivery, CancellationToken cancellationToken)
    {
        if (delivery.Body.Length > 128 * 1024) throw new RejectDeliveryException("Result exceeds 128 KiB.");
        string raw;
        try { raw = new UTF8Encoding(false, true).GetString(delivery.Body); }
        catch (DecoderFallbackException) { throw new RejectDeliveryException("Result is not valid UTF-8."); }
        InspectionProcessedV1 result;
        try
        {
            result = MessageJson.Deserialize<InspectionProcessedV1>(raw)
                ?? throw new RejectDeliveryException("Result JSON is null.");
        }
        catch (JsonException exception)
        {
            throw new RejectDeliveryException($"Malformed result JSON: {exception.Message}");
        }
        ValidateResultEnvelope(result, delivery);
        await using var database = await factory.CreateDbContextAsync(cancellationToken);
        var source = await database.Messages.AsNoTracking()
            .SingleOrDefaultAsync(item => item.MessageId == result.MessageId, cancellationToken);
        if (source is null || source.RunId != result.SimulationId)
            throw new RejectDeliveryException("Result does not match a simulator source MessageId and SimulationId.");
        if (await database.Messages.AsNoTracking().AnyAsync(item => item.ResultId == result.ResultId &&
                item.MessageId != result.MessageId, cancellationToken))
            throw new RejectDeliveryException("ResultId has already been assigned to another source message.");
        if (result.Status == "Processed" && (result.BatchId != source.BatchId || result.BatchCode != source.BatchCode || result.Stage != source.Stage))
            throw new RejectDeliveryException("Processed result batch or stage differs from the source payload.");
        if (result.Status == "Rejected" && result.BatchId != Guid.Empty && result.BatchId != source.BatchId)
            throw new RejectDeliveryException("Rejected result identifies a different batch.");
        if (source.ResultJson is not null)
        {
            logger.LogInformation("Ignoring repeated result for source message {MessageId}; the first result is retained", result.MessageId);
            return;
        }
        // Preserve the first result even if multiple deliveries race.
        var stored = await database.Messages.Where(item => item.MessageId == result.MessageId && item.ResultJson == null)
            .ExecuteUpdateAsync(update => update
                .SetProperty(item => item.ResultId, result.ResultId)
                .SetProperty(item => item.ReceivedAt, DateTime.UtcNow)
                .SetProperty(item => item.Status, result.Status)
                .SetProperty(item => item.Outcome, result.Outcome)
                .SetProperty(item => item.ResultJson, raw), cancellationToken);
        if (stored == 1)
            logger.LogInformation("Stored simulator result {Status}/{Outcome} for {MessageId}", result.Status, result.Outcome, result.MessageId);
    }

    private static void ValidateResultEnvelope(InspectionProcessedV1 result, BrokerDelivery delivery)
    {
        if (result.SchemaVersion != 1 || result.ResultId == Guid.Empty || result.MessageId == Guid.Empty ||
            result.SimulationId == Guid.Empty || result.ProcessedAt == default)
            throw new RejectDeliveryException("Result schema, identifiers or processing time are invalid.");
        if (!Guid.TryParse(delivery.MessageId, out var transportId) || transportId != result.ResultId ||
            !Guid.TryParse(delivery.CorrelationId, out var correlationId) || correlationId != result.MessageId)
            throw new RejectDeliveryException("Result transport headers do not match ResultId and source MessageId.");
        if (result.Samples is null || result.Errors is null || result.NonconformityCount < 0)
            throw new RejectDeliveryException("Result collections or nonconformity count are invalid.");
        var validProcessed = result.Status == "Processed" &&
            result.Outcome is "Conformant" or "Nonconformant" && result.Errors.Count == 0;
        var validRejected = result.Status == "Rejected" && result.Outcome is null && result.Errors.Count > 0 &&
            result.Errors.All(issue => issue is not null && !string.IsNullOrWhiteSpace(issue.Code) &&
                !string.IsNullOrWhiteSpace(issue.Field) && !string.IsNullOrWhiteSpace(issue.Message));
        if (!validProcessed && !validRejected)
            throw new RejectDeliveryException("Result status, outcome and validation errors are inconsistent.");
        if (validProcessed)
        {
            if (result.Samples.Count != 3 || result.Samples.Any(sample => sample is null ||
                    string.IsNullOrWhiteSpace(sample.Code) || sample.Criteria is null || sample.Criteria.Count != 3 ||
                    sample.Criteria.Any(criterion => criterion is null) ||
                    sample.IsConformant != sample.Criteria.All(criterion => criterion.IsConformant)) ||
                result.Samples.Select(sample => sample.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 3)
                throw new RejectDeliveryException("Processed result does not contain three complete and consistent sample evaluations.");
            var failures = result.Samples.Sum(sample => sample.Criteria.Count(criterion => !criterion.IsConformant));
            if (result.NonconformityCount != failures || (result.Outcome == "Conformant") != (failures == 0))
                throw new RejectDeliveryException("Processed result outcome and nonconformity count disagree with its evaluations.");
        }
    }
}
