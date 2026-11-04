using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Qraft.Contracts;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Qraft.Messaging;

public sealed record BrokerDelivery(string MessageId, string? CorrelationId, byte[] Body);

public sealed class RejectDeliveryException(string message) : Exception(message);

public sealed class RabbitMqBus(IConfiguration configuration, ILogger<RabbitMqBus> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly SemaphoreSlim _publishLock = new(1, 1);
    private IConnection? _connection;
    private IChannel? _publisher;

    public bool IsConnected => _connection?.IsOpen == true;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (IsConnected) return;
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (IsConnected) return;
            if (_connection is not null) await _connection.DisposeAsync();
            var factory = new ConnectionFactory
            {
                HostName = configuration["RabbitMq:HostName"] ?? "localhost",
                Port = configuration.GetValue("RabbitMq:Port", 5677),
                UserName = configuration["RabbitMq:UserName"] ?? "qraft",
                Password = configuration["RabbitMq:Password"] ?? "qraft-local-dev",
                VirtualHost = configuration["RabbitMq:VirtualHost"] ?? "/qraft",
                AutomaticRecoveryEnabled = false,
                ConsumerDispatchConcurrency = 1,
                RequestedConnectionTimeout = TimeSpan.FromSeconds(5)
            };
            _connection = await factory.CreateConnectionAsync(cancellationToken);
            await using var topology = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
            await DeclareQueuesAsync(topology, cancellationToken);
            logger.LogInformation("RabbitMQ connected at {Host}:{Port}", factory.HostName, factory.Port);
        }
        finally { _connectionLock.Release(); }
    }

    public async Task PublishAsync(string queue, string json, string messageId, string? correlationId,
        CancellationToken cancellationToken)
    {
        await _publishLock.WaitAsync(cancellationToken);
        try
        {
            await StartAsync(cancellationToken);
            if (_publisher?.IsOpen != true)
            {
                if (_publisher is not null) await _publisher.DisposeAsync();
                _publisher = await _connection!.CreateChannelAsync(
                    new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
                    cancellationToken);
            }
            var properties = new BasicProperties
            {
                ContentType = "application/json",
                Persistent = true,
                MessageId = messageId,
                CorrelationId = correlationId
            };
            await _publisher.BasicPublishAsync("", queue, mandatory: true, properties,
                Encoding.UTF8.GetBytes(json), cancellationToken);
        }
        finally { _publishLock.Release(); }
    }

    public async Task ConsumeAsync(string queue, Func<BrokerDelivery, CancellationToken, Task> handler,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await StartAsync(cancellationToken);
                await using var channel = await _connection!.CreateChannelAsync(cancellationToken: cancellationToken);
                await channel.BasicQosAsync(0, 10, false, cancellationToken);
                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += async (_, delivery) =>
                {
                    var body = delivery.Body.ToArray();
                    try
                    {
                        await handler(new BrokerDelivery(delivery.BasicProperties.MessageId ?? "",
                            delivery.BasicProperties.CorrelationId, body), cancellationToken);
                        await channel.BasicAckAsync(delivery.DeliveryTag, false, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
                    catch (RejectDeliveryException exception)
                    {
                        logger.LogWarning("Message {MessageId} dead-lettered: {Reason}",
                            delivery.BasicProperties.MessageId, exception.Message);
                        await channel.BasicRejectAsync(delivery.DeliveryTag, false, cancellationToken);
                    }
                    catch (Exception exception)
                    {
                        logger.LogWarning(exception, "Message {MessageId} will be retried", delivery.BasicProperties.MessageId);
                        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                        await channel.BasicNackAsync(delivery.DeliveryTag, false, true, cancellationToken);
                    }
                };
                await channel.BasicConsumeAsync(queue, false, consumer, cancellationToken);
                logger.LogInformation("Consuming {Queue}", queue);
                while (channel.IsOpen && !cancellationToken.IsCancellationRequested)
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Consumer {Queue} disconnected; reconnecting", queue);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
    }

    private static async Task DeclareQueuesAsync(IChannel channel, CancellationToken cancellationToken)
    {
        await channel.QueueDeclareAsync(QueueNames.DeadLetters, true, false, false,
            cancellationToken: cancellationToken);
        var arguments = new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = "",
            ["x-dead-letter-routing-key"] = QueueNames.DeadLetters
        };
        foreach (var name in new[] { QueueNames.Submissions, QueueNames.Results })
            await channel.QueueDeclareAsync(name, true, false, false, arguments, cancellationToken: cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_publisher is not null) await _publisher.DisposeAsync();
        if (_connection is not null) await _connection.DisposeAsync();
        _publishLock.Dispose();
        _connectionLock.Dispose();
    }
}

public static class MessagingRegistration
{
    public static IServiceCollection AddQraftMessaging(this IServiceCollection services) =>
        services.AddSingleton<RabbitMqBus>();
}
