using System.Text;
using Microsoft.AspNetCore.SignalR;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Support.Api.Realtime;

namespace Support.Api.Services;

public sealed class NotificationConsumer(IConfiguration configuration, IHubContext<TicketHub> hub, ILogger<NotificationConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory { Uri = new Uri(configuration["RabbitMq:Uri"] ?? "amqp://guest:guest@localhost:5672/"), DispatchConsumersAsync = true };
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var connection = factory.CreateConnection();
                using var channel = connection.CreateModel();
                channel.ExchangeDeclare("support.events", ExchangeType.Topic, durable: true);
                channel.QueueDeclare("support.notifications", durable: true, exclusive: false, autoDelete: false);
                channel.QueueBind("support.notifications", "support.events", "ticket.*");
                channel.BasicQos(0, 1, false);
                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.Received += async (_, ea) =>
                {
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(Encoding.UTF8.GetString(ea.Body.ToArray()));
                        var root = doc.RootElement;
                        var requester = GetGuid(root, "RequesterId", "requesterId");
                        var ticket = GetGuid(root, "TicketId", "ticketId");
                        if (requester == Guid.Empty || ticket == Guid.Empty) throw new InvalidOperationException("Event is missing ticket or requester ID.");
                        await hub.Clients.Group($"user:{requester}").SendAsync("ticket.updated", new { ticketId = ticket, eventType = ea.BasicProperties.Type, data = root }, stoppingToken);
                        channel.BasicAck(ea.DeliveryTag, false);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Notification event failed.");
                        try { channel.BasicNack(ea.DeliveryTag, false, requeue: true); } catch { }
                    }
                };
                channel.BasicConsume("support.notifications", autoAck: false, consumer);
                logger.LogInformation("Notification service is consuming support events.");
                while (!stoppingToken.IsCancellationRequested && connection.IsOpen)
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Notification service could not connect to RabbitMQ; retrying.");
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }

    private static Guid GetGuid(System.Text.Json.JsonElement element, params string[] names)
    {
        foreach (var name in names) if (element.TryGetProperty(name, out var value) && value.TryGetGuid(out var id)) return id;
        return Guid.Empty;
    }
}
