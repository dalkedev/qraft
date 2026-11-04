using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Qraft.Messaging;
using Qraft.Simulator;
using Qraft.Simulator.Data;

var builder = WebApplication.CreateBuilder(args);
if (builder.Configuration["Urls"] is null) builder.WebHost.UseUrls("http://localhost:5281");

var configuredPath = builder.Configuration["Data:DatabasePath"];
var dataDirectory = Environment.GetEnvironmentVariable("QRAFT_DATA_DIR")
    ?? Path.Combine(builder.Environment.ContentRootPath, "data");
var databasePath = Path.GetFullPath(string.IsNullOrWhiteSpace(configuredPath)
    ? Path.Combine(dataDirectory, "simulator.db") : configuredPath, builder.Environment.ContentRootPath);
Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
var connectionString = new SqliteConnectionStringBuilder
{
    DataSource = databasePath, ForeignKeys = true, DefaultTimeout = 10
}.ToString();

builder.Services.AddOpenApi();
builder.Services.AddDbContextFactory<SimulatorDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddQraftMessaging();
builder.Services.AddSingleton<SimulationStore>();
builder.Services.AddHostedService<SubmissionOutboxWorker>();
builder.Services.AddHostedService<ResultConsumerWorker>();

var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<SimulatorDbContext>>();
    await using var database = await factory.CreateDbContextAsync();
    await database.Database.MigrateAsync();
    await ScenarioFixtures.SeedAsync(database, CancellationToken.None);
}

app.MapOpenApi();
app.MapGet("/health/live", () => Results.Ok(new { status = "ok", application = "qraft-simulator" }));
app.MapGet("/health/ready", async (IDbContextFactory<SimulatorDbContext> factory, RabbitMqBus bus, CancellationToken cancellationToken) =>
{
    var databaseReady = false;
    try
    {
        await using var database = await factory.CreateDbContextAsync(cancellationToken);
        databaseReady = await database.Database.CanConnectAsync(cancellationToken) &&
            await database.Fixtures.CountAsync(cancellationToken) == 5;
    }
    catch (Exception exception) when (exception is not OperationCanceledException) { databaseReady = false; }
    var ready = databaseReady && bus.IsConnected;
    return Results.Json(new { status = ready ? "ready" : "not-ready", database = databaseReady, broker = bus.IsConnected },
        statusCode: ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
});

app.MapGet("/api/scenarios", async (SimulationStore store, CancellationToken cancellationToken) =>
    Results.Ok(await store.GetScenariosAsync(cancellationToken)));
app.MapPost("/api/runs", async (CreateRunRequest request, SimulationStore store, CancellationToken cancellationToken) =>
{
    if (request.Count is < 1 or > 20)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["count"] = ["Liczba komunikatów musi wynosić od 1 do 20."] });
    var scenario = request.Scenario?.Trim().ToLowerInvariant();
    if (string.IsNullOrEmpty(scenario))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["scenario"] = ["Wybierz scenariusz."] });
    var run = await store.CreateRunAsync(scenario, request.Count, cancellationToken);
    return run is null
        ? Results.ValidationProblem(new Dictionary<string, string[]> { ["scenario"] = ["Nieznany scenariusz."] })
        : Results.Created($"/api/runs/{run.Id}", run);
});
app.MapGet("/api/runs", async (SimulationStore store, CancellationToken cancellationToken) =>
    Results.Ok(await store.GetRunsAsync(cancellationToken)));
app.MapGet("/api/runs/{id:guid}", async (Guid id, SimulationStore store, CancellationToken cancellationToken) =>
{
    var run = await store.GetRunAsync(id, cancellationToken);
    return run is null ? Results.NotFound() : Results.Ok(run);
});
app.MapPost("/api/messages/{messageId:guid}/replay", async (Guid messageId, SimulationStore store, CancellationToken cancellationToken) =>
{
    var record = await store.ReplayAsync(messageId, cancellationToken);
    return record is null ? Results.NotFound() : Results.Accepted($"/api/runs/{record.SimulationId}", record);
});

app.Logger.LogInformation("Simulator SQLite source and outbox: {DatabasePath}", databasePath);
await app.RunAsync();

public partial class Program;
