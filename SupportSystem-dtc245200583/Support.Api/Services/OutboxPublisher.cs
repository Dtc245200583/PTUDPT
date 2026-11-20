using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using Support.Api.Infrastructure;

namespace Support.Api.Services;

public sealed class OutboxPublisher(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<OutboxPublisher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<SupportDbContext>();
                var pending = await db.OutboxMessages.Where(x => x.PublishedAt == null).OrderBy(x => x.OccurredAt).Take(50).ToListAsync(stoppingToken);
                if (pending.Count > 0)
                {
                    var factory = new ConnectionFactory { Uri = new Uri(configuration["RabbitMq:Uri"] ?? "amqp://guest:guest@localhost:5672/"), DispatchConsumersAsync = true };
                    using var connection = factory.CreateConnection();
                    using var channel = connection.CreateModel();
                    channel.ExchangeDeclare("support.events", ExchangeType.Topic, durable: true);
                    channel.QueueDeclare("support.assignment", durable: true, exclusive: false, autoDelete: false);
                    channel.QueueBind("support.assignment", "support.events", "ticket.created");
                    channel.QueueDeclare("support.notifications", durable: true, exclusive: false, autoDelete: false);
                    channel.QueueBind("support.notifications", "support.events", "ticket.*");
                    channel.ConfirmSelect();
                    foreach (var item in pending)
                    {
                        var route = item.Type switch { "TicketCreated.v1" => "ticket.created", "TicketAssigned.v1" => "ticket.assigned", _ => "ticket.status_changed" };
                        var properties = channel.CreateBasicProperties(); properties.Persistent = true; properties.MessageId = item.Id.ToString(); properties.Type = item.Type;
                        var bytes = System.Text.Encoding.UTF8.GetBytes(item.Payload);
                        channel.BasicPublish("support.events", route, properties, bytes);
                        item.PublishedAt = DateTimeOffset.UtcNow;
                    }
                    channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5));
                    await db.SaveChangesAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning(ex, "Outbox publish attempt failed; pending messages will be retried."); }
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }
}
