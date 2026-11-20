using System.Text;
using System.Text.Json;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Support.Contracts;

namespace Support.AssignmentWorker;

public sealed class Worker(IConfiguration configuration, ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory { Uri = new Uri(configuration["RabbitMq:Uri"] ?? "amqp://guest:guest@localhost:5672/"), DispatchConsumersAsync = true };
        var delay = TimeSpan.FromSeconds(3);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var connection = factory.CreateConnection();
                using var channel = connection.CreateModel();
                channel.ExchangeDeclare("support.events", ExchangeType.Topic, durable: true);
                channel.QueueDeclare("support.assignment", durable: true, exclusive: false, autoDelete: false);
                channel.QueueBind("support.assignment", "support.events", "ticket.created");
                channel.BasicQos(0, 1, false);
                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.Received += async (_, ea) =>
                {
                    try
                    {
                        var message = JsonSerializer.Deserialize<TicketCreatedV1>(Encoding.UTF8.GetString(ea.Body.ToArray()));
                        if (message is null) throw new InvalidOperationException("Invalid TicketCreated payload.");
                        await AssignAsync(message, stoppingToken);
                        channel.BasicAck(ea.DeliveryTag, false);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Assignment failed for RabbitMQ delivery {DeliveryTag}.", ea.DeliveryTag);
                        channel.BasicNack(ea.DeliveryTag, false, requeue: true);
                    }
                };
                channel.BasicConsume("support.assignment", autoAck: false, consumer);
                logger.LogInformation("Assignment worker is consuming ticket.created events.");
                while (!stoppingToken.IsCancellationRequested && connection.IsOpen)
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "RabbitMQ unavailable; assignment worker will reconnect.");
                await Task.Delay(delay, stoppingToken);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
            }
        }
    }

    private async Task AssignAsync(TicketCreatedV1 message, CancellationToken ct)
    {
        var connectionString = configuration.GetConnectionString("SupportDb") ?? throw new InvalidOperationException("ConnectionStrings:SupportDb is required.");
        var defaultTeamId = Guid.TryParse(configuration["Assignment:DefaultTeamId"], out var configured) ? configured : Guid.Parse("11111111-1111-1111-1111-111111111111");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using (var duplicate = new NpgsqlCommand("SELECT 1 FROM \"ProcessedMessages\" WHERE \"ConsumerName\" = 'assignment' AND \"MessageId\" = @id", connection, transaction))
        {
            duplicate.Parameters.AddWithValue("id", message.MessageId);
            if (await duplicate.ExecuteScalarAsync(ct) is not null) { await transaction.CommitAsync(ct); return; }
        }

        const string category = "General";
        Guid requesterId; string status; int version; Guid? teamId;
        await using (var update = new NpgsqlCommand("""
            UPDATE "Tickets" SET "Category" = @category, "TeamId" = @team,
                "Status" = 'Assigned', "AssignedAt" = now(), "UpdatedAt" = now(), "Version" = "Version" + 1
            WHERE "Id" = @ticket AND "Status" = 'New'
            RETURNING "RequesterId", "Status", "Version", "TeamId"
            """, connection, transaction))
        {
            update.Parameters.AddWithValue("category", category); update.Parameters.AddWithValue("team", defaultTeamId); update.Parameters.AddWithValue("ticket", message.TicketId);
            await using var reader = await update.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                await reader.DisposeAsync();
                await using var mark = new NpgsqlCommand("INSERT INTO \"ProcessedMessages\" (\"ConsumerName\", \"MessageId\", \"ProcessedAt\") VALUES ('assignment', @id, now()) ON CONFLICT DO NOTHING", connection, transaction);
                mark.Parameters.AddWithValue("id", message.MessageId); await mark.ExecuteNonQueryAsync(ct);
                await transaction.CommitAsync(ct); return;
            }
            requesterId = reader.GetGuid(0); status = reader.GetString(1); version = reader.GetInt32(2); teamId = reader.IsDBNull(3) ? null : reader.GetGuid(3);
        }
        var now = DateTimeOffset.UtcNow;
        await using (var history = new NpgsqlCommand("""
            INSERT INTO "TicketHistory" ("TicketId", "ActorId", "Action", "FieldName", "NewValue", "OccurredAt", "CorrelationId")
            VALUES (@ticket, NULL, 'TicketAssigned', 'TeamId', @team, @now, @correlation)
            """, connection, transaction))
        {
            history.Parameters.AddWithValue("ticket", message.TicketId); history.Parameters.AddWithValue("team", defaultTeamId.ToString());
            history.Parameters.AddWithValue("now", now); history.Parameters.AddWithValue("correlation", message.MessageId); await history.ExecuteNonQueryAsync(ct);
        }
        var evt = new TicketAssignedV1(Guid.NewGuid(), message.TicketId, requesterId, teamId, null, category, status, version, now);
        await using (var outbox = new NpgsqlCommand("""
            INSERT INTO "OutboxMessages" ("Id", "Type", "AggregateId", "Payload", "OccurredAt", "Attempts")
            VALUES (@id, 'TicketAssigned.v1', @ticket, @payload, @now, 0)
            """, connection, transaction))
        {
            outbox.Parameters.AddWithValue("id", evt.MessageId); outbox.Parameters.AddWithValue("ticket", message.TicketId);
            outbox.Parameters.AddWithValue("payload", JsonSerializer.Serialize(evt)); outbox.Parameters.AddWithValue("now", now);
            await outbox.ExecuteNonQueryAsync(ct);
        }
        await using (var mark = new NpgsqlCommand("INSERT INTO \"ProcessedMessages\" (\"ConsumerName\", \"MessageId\", \"ProcessedAt\") VALUES ('assignment', @id, @now)", connection, transaction))
        {
            mark.Parameters.AddWithValue("id", message.MessageId); mark.Parameters.AddWithValue("now", now); await mark.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
        logger.LogInformation("Assigned ticket {TicketId} to team {TeamId}.", message.TicketId, defaultTeamId);
    }
}
