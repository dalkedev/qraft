using Microsoft.EntityFrameworkCore;
using Qraft.Application;
using Qraft.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddQraftPersistence(builder.Configuration);
builder.Services.AddOpenApi();
var app = builder.Build();
await app.Services.InitializeQraftDatabaseAsync();
app.MapOpenApi();

app.MapGet("/health/live", () => Results.Ok(new { status = "ok", service = "qraft-api" }));
app.MapGet("/health/ready", async (QualityDbContext database, CancellationToken cancellationToken) =>
{
    try
    {
        await database.ReceivedInspections.CountAsync(cancellationToken);
        return Results.Ok(new { status = "ready", database = "connected" });
    }
    catch (Exception) { return Results.Json(new { status = "not-ready", database = "unavailable" }, statusCode: 503); }
});
app.MapGet("/api/inspections", async (int? limit, IQualityStore store, CancellationToken cancellationToken) =>
    Results.Ok(await store.RecentAsync(limit ?? 100, cancellationToken)));
app.MapGet("/api/inspections/{messageId:guid}", async (Guid messageId, IQualityStore store, CancellationToken cancellationToken) =>
{
    var record = await store.FindAsync(messageId, cancellationToken);
    return record is null ? Results.NotFound() : Results.Ok(record.Result);
});
app.MapGet("/api/inspections/{messageId:guid}/source", async (Guid messageId, QualityDbContext database, CancellationToken cancellationToken) =>
{
    var row = await database.ReceivedInspections.AsNoTracking().SingleOrDefaultAsync(x => x.MessageId == messageId, cancellationToken);
    return row is null ? Results.NotFound() : Results.Ok(new { row.MessageId, row.PayloadHash, row.PayloadJson, row.ReceivedAtUtc });
});
app.MapGet("/api/batches", async (IQualityStore store, CancellationToken cancellationToken) =>
{
    var results = await store.RecentAsync(500, cancellationToken);
    return Results.Ok(results.Where(x => x.Status == "Processed").GroupBy(x => x.BatchId).Select(group =>
    {
        var latest = group.OrderByDescending(x => x.ProcessedAt).First();
        return new { id = group.Key, latest.BatchCode, latest.ProductName, latest.Stage, latest.Outcome, inspectionCount = group.Count() };
    }));
});
app.MapGet("/api/summary", async (QualityDbContext database, CancellationToken cancellationToken) => Results.Ok(new
{
    received = await database.ReceivedInspections.CountAsync(cancellationToken),
    processed = await database.ReceivedInspections.CountAsync(x => x.Status == "Processed", cancellationToken),
    rejected = await database.ReceivedInspections.CountAsync(x => x.Status == "Rejected", cancellationToken),
    nonconformant = await database.ReceivedInspections.CountAsync(x => x.Outcome == "Nonconformant", cancellationToken),
    pendingResults = await database.ResultOutbox.CountAsync(x => x.PublishedAtUtc == null, cancellationToken)
}));
await app.RunAsync();

public partial class Program;
