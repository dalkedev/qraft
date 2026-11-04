using Microsoft.EntityFrameworkCore;
using Qraft.Simulator.Data;

namespace Qraft.Simulator;

public sealed record ScenarioDto(string Code, string Name, string Description, string ExpectedStatus, string? ExpectedOutcome);
public sealed record CreateRunRequest(string? Scenario, int Count);
public sealed record RunStats(int Total, int Pending, int Published, int Processed, int Rejected, int Conformant, int Nonconformant);
public sealed record SourceMessageDto(Guid MessageId, Guid SimulationId, Guid BatchId, string BatchCode,
    string Stage, string Scenario, string PublishStatus, int PublishAttempts, DateTime? PublishedAt,
    string? LastPublishError, DateTime? ReceivedAt, string? Status, string? Outcome, Guid? ResultId,
    string PayloadJson, string? ResultJson, int ReplayCount);
public sealed record RunDto(Guid Id, string Scenario, int Count, DateTime CreatedAt, RunStats Stats,
    IReadOnlyList<SourceMessageDto> Records);

public sealed class SimulationStore(IDbContextFactory<SimulatorDbContext> factory)
{
    public async Task<IReadOnlyList<ScenarioDto>> GetScenariosAsync(CancellationToken cancellationToken)
    {
        await using var database = await factory.CreateDbContextAsync(cancellationToken);
        return await database.Fixtures.AsNoTracking().OrderBy(item => item.Code)
            .Select(item => new ScenarioDto(item.Code, item.Name, item.Description, item.ExpectedStatus, item.ExpectedOutcome))
            .ToListAsync(cancellationToken);
    }

    public async Task<RunDto?> CreateRunAsync(string scenario, int count, CancellationToken cancellationToken)
    {
        await using var database = await factory.CreateDbContextAsync(cancellationToken);
        var fixture = await database.Fixtures.AsNoTracking().SingleOrDefaultAsync(item => item.Code == scenario, cancellationToken);
        if (fixture is null) return null;
        var now = DateTimeOffset.UtcNow;
        var run = new SimulationRun { Id = Guid.NewGuid(), ScenarioCode = scenario, Count = count, CreatedAt = now.UtcDateTime };
        database.Runs.Add(run);
        for (var index = 0; index < count; index++)
        {
            var messageId = Guid.NewGuid();
            var batchId = Guid.NewGuid();
            var batchCode = $"SIM-{now:yyyyMMdd-HHmmss}-{messageId.ToString("N")[..8].ToUpperInvariant()}";
            database.Messages.Add(new SourceMessage
            {
                MessageId = messageId, RunId = run.Id, BatchId = batchId, BatchCode = batchCode,
                Stage = "E1", CreatedAt = now.UtcDateTime,
                PayloadJson = ScenarioFixtures.CreatePayload(fixture, messageId, run.Id, batchId, batchCode, now)
            });
            database.Outbox.Add(NewOutbox(messageId, now.UtcDateTime));
        }
        // SaveChanges wraps run, source records and outbox entries in a single transaction.
        await database.SaveChangesAsync(cancellationToken);
        return await GetRunAsync(run.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<RunDto>> GetRunsAsync(CancellationToken cancellationToken)
    {
        await using var database = await factory.CreateDbContextAsync(cancellationToken);
        var runs = await database.Runs.AsNoTracking().OrderByDescending(item => item.CreatedAt).Take(50).ToListAsync(cancellationToken);
        return await BuildRunsAsync(database, runs, cancellationToken);
    }

    public async Task<RunDto?> GetRunAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var database = await factory.CreateDbContextAsync(cancellationToken);
        var run = await database.Runs.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return run is null ? null : (await BuildRunsAsync(database, [run], cancellationToken)).Single();
    }

    public async Task<SourceMessageDto?> ReplayAsync(Guid messageId, CancellationToken cancellationToken)
    {
        await using var database = await factory.CreateDbContextAsync(cancellationToken);
        var source = await database.Messages.AsNoTracking().SingleOrDefaultAsync(item => item.MessageId == messageId, cancellationToken);
        if (source is null) return null;
        database.Outbox.Add(NewOutbox(messageId, DateTime.UtcNow));
        await database.SaveChangesAsync(cancellationToken);
        var run = await GetRunAsync(source.RunId, cancellationToken);
        return run!.Records.Single(item => item.MessageId == messageId);
    }

    private static SubmissionOutbox NewOutbox(Guid messageId, DateTime now) => new()
    {
        Id = Guid.NewGuid(), SourceMessageId = messageId, CreatedAt = now, NextAttemptAt = now
    };

    private static async Task<IReadOnlyList<RunDto>> BuildRunsAsync(SimulatorDbContext database,
        IReadOnlyList<SimulationRun> runs, CancellationToken cancellationToken)
    {
        var runIds = runs.Select(item => item.Id).ToArray();
        var messages = await database.Messages.AsNoTracking().Where(item => runIds.Contains(item.RunId))
            .OrderBy(item => item.CreatedAt).ThenBy(item => item.BatchCode).ToListAsync(cancellationToken);
        var messageIds = messages.Select(item => item.MessageId).ToArray();
        var outbox = await database.Outbox.AsNoTracking().Where(item => messageIds.Contains(item.SourceMessageId))
            .ToListAsync(cancellationToken);
        var publications = outbox.ToLookup(item => item.SourceMessageId);
        return runs.Select(run =>
        {
            var records = messages.Where(item => item.RunId == run.Id).Select(source =>
            {
                var attempts = publications[source.MessageId].ToArray();
                var pending = attempts.Any(item => item.PublishedAt is null);
                var publishedAt = attempts.Where(item => item.PublishedAt.HasValue).Select(item => item.PublishedAt).Max();
                var lastError = attempts.Where(item => item.PublishedAt is null && item.LastError is not null)
                    .OrderByDescending(item => item.NextAttemptAt).Select(item => item.LastError).FirstOrDefault();
                return new SourceMessageDto(source.MessageId, source.RunId, source.BatchId, source.BatchCode,
                    source.Stage, run.ScenarioCode, pending ? "Pending" : "Published", attempts.Sum(item => item.Attempts),
                    AsUtc(publishedAt), lastError, AsUtc(source.ReceivedAt), source.Status, source.Outcome, source.ResultId,
                    source.PayloadJson, source.ResultJson, Math.Max(0, attempts.Length - 1));
            }).ToArray();
            var stats = new RunStats(records.Length, records.Count(item => item.PublishStatus == "Pending"),
                records.Count(item => item.PublishStatus == "Published"), records.Count(item => item.Status == "Processed"),
                records.Count(item => item.Status == "Rejected"), records.Count(item => item.Outcome == "Conformant"),
                records.Count(item => item.Outcome == "Nonconformant"));
            return new RunDto(run.Id, run.ScenarioCode, run.Count, DateTime.SpecifyKind(run.CreatedAt, DateTimeKind.Utc), stats, records);
        }).ToArray();
    }

    private static DateTime? AsUtc(DateTime? value) => value.HasValue
        ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;
}
