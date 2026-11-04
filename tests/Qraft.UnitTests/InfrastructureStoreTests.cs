using Microsoft.EntityFrameworkCore;
using Qraft.Contracts;
using Qraft.Infrastructure;

namespace Qraft.UnitTests;

public sealed class InfrastructureStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "qraft-tests-" + Guid.NewGuid());

    private QualityDbContext CreateDatabase()
    {
        Directory.CreateDirectory(_directory);
        var options = new DbContextOptionsBuilder<QualityDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_directory, "qraft.db")};Foreign Keys=True").Options;
        return new QualityDbContext(options);
    }

    [Fact]
    public async Task IdenticalReplayKeepsOneInspectionAndOneOutboxMessage()
    {
        await using var database = CreateDatabase();
        await database.Database.MigrateAsync();
        var store = new EfQualityStore(database);
        var result = Result();
        await store.SaveAsync(null, result, "{}", "same-hash");
        var replay = await store.SaveAsync(null, result with { ResultId = Guid.NewGuid() }, "{}", "same-hash");
        Assert.Equal(result.ResultId, replay.ResultId);
        Assert.Equal(1, await database.ReceivedInspections.CountAsync());
        Assert.Equal(1, await database.ResultOutbox.CountAsync());
        Assert.Equal(1, await database.SampleAssessments.CountAsync());
    }

    [Fact]
    public async Task ReusedMessageIdWithDifferentPayloadCannotOverwriteHistory()
    {
        await using var database = CreateDatabase();
        await database.Database.MigrateAsync();
        var store = new EfQualityStore(database);
        var result = Result();
        await store.SaveAsync(null, result, "original", "first-hash");
        await Assert.ThrowsAsync<MessageIdentityConflictException>(() =>
            store.SaveAsync(null, result, "changed", "second-hash"));
        Assert.Equal("original", (await database.ReceivedInspections.SingleAsync()).PayloadJson);
    }

    [Fact]
    public async Task ReopeningDatabasePreservesResultAndPendingReply()
    {
        var result = Result();
        await using (var database = CreateDatabase())
        {
            await database.Database.MigrateAsync();
            await new EfQualityStore(database).SaveAsync(null, result, "{}", "hash");
        }
        await using var reopened = CreateDatabase();
        var saved = await new EfQualityStore(reopened).FindAsync(result.MessageId);
        Assert.NotNull(saved);
        Assert.Equal(result.ResultId, saved.Result.ResultId);
        Assert.Null((await reopened.ResultOutbox.SingleAsync()).PublishedAtUtc);
    }

    [Fact]
    public async Task FailedWriteDoesNotLeavePartialInboxOrOutbox()
    {
        await using (var database = CreateDatabase())
        {
            await database.Database.MigrateAsync();
            var result = Result();
            result = result with { Samples = [result.Samples[0], result.Samples[0]] };
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                new EfQualityStore(database).SaveAsync(null, result, "{}", "hash"));
        }
        await using var reopened = CreateDatabase();
        Assert.Equal(0, await reopened.ReceivedInspections.CountAsync());
        Assert.Equal(0, await reopened.ResultOutbox.CountAsync());
        Assert.Equal(0, await reopened.SampleAssessments.CountAsync());
    }

    private static InspectionProcessedV1 Result() => new()
    {
        ResultId = Guid.NewGuid(), MessageId = Guid.NewGuid(), SimulationId = Guid.NewGuid(),
        BatchId = Guid.NewGuid(), BatchCode = "TEST-001", ProductName = "Regranulat PP", Stage = "E1",
        Attempt = 1, Status = "Processed", Outcome = "Conformant", ProcessedAt = DateTimeOffset.UtcNow,
        Samples = [new SampleEvaluationV1("P01", true, [])]
    };

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
