using Qraft.Application;
using Qraft.Infrastructure;
using Qraft.Messaging;
using Qraft.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddQraftPersistence(builder.Configuration);
builder.Services.AddQraftMessaging();
builder.Services.AddSingleton<InspectionProcessor>();
builder.Services.AddHostedService<InspectionConsumer>();
builder.Services.AddHostedService<ResultOutboxPublisher>();
using var host = builder.Build();
await host.Services.InitializeQraftDatabaseAsync();
await host.RunAsync();
