using Microsoft.EntityFrameworkCore;
using Qraft.Application;
using Qraft.Contracts;

namespace Qraft.Infrastructure;

public sealed class MessageIdentityConflictException(Guid messageId)
    : Exception($"Message {messageId} was already saved with a different payload.");

public sealed class EfQualityStore(QualityDbContext database) : IQualityStore
{
    public async Task<StoredProcessing?> FindAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var row = await database.ReceivedInspections.AsNoTracking()
            .SingleOrDefaultAsync(x => x.MessageId == messageId, cancellationToken);
        return row is null ? null : new StoredProcessing(row.MessageId, row.PayloadHash,
            MessageJson.Deserialize<InspectionProcessedV1>(row.ResultJson)!);
    }

    public async Task<IReadOnlyList<InspectionProcessedV1>> RecentAsync(int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var rows = await database.ReceivedInspections.AsNoTracking().OrderByDescending(x => x.ReceivedAtUtc)
            .Take(Math.Clamp(limit, 1, 500)).Select(x => x.ResultJson).ToListAsync(cancellationToken);
        return rows.Select(x => MessageJson.Deserialize<InspectionProcessedV1>(x)!).ToArray();
    }

    public async Task<InspectionProcessedV1> SaveAsync(InspectionSubmittedV1? submission,
        InspectionProcessedV1 result, string rawJson, string payloadHash,
        CancellationToken cancellationToken = default)
    {
        var existing = await FindAsync(result.MessageId, cancellationToken);
        if (existing is not null)
        {
            if (existing.PayloadHash != payloadHash) throw new MessageIdentityConflictException(result.MessageId);
            return existing.Result;
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var row = new ReceivedInspection
        {
            MessageId = result.MessageId,
            PayloadHash = payloadHash,
            PayloadJson = rawJson,
            ResultJson = MessageJson.Serialize(result),
            SimulationId = result.SimulationId,
            BatchId = result.BatchId,
            BatchCode = result.BatchCode,
            Stage = result.Stage,
            Attempt = result.Attempt,
            Status = result.Status,
            Outcome = result.Outcome,
            ReceivedAtUtc = now,
            Samples = result.Samples.Select(sample => new StoredSampleAssessment
            {
                Id = Guid.NewGuid(), MessageId = result.MessageId, Code = sample.Code,
                IsConformant = sample.IsConformant, CriteriaJson = MessageJson.Serialize(sample.Criteria)
            }).ToList()
        };
        database.ReceivedInspections.Add(row);
        database.ResultOutbox.Add(new ResultOutboxMessage
        {
            Id = result.ResultId, SourceMessageId = result.MessageId,
            PayloadJson = row.ResultJson, CreatedAtUtc = now, NextAttemptAtUtc = now
        });
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
