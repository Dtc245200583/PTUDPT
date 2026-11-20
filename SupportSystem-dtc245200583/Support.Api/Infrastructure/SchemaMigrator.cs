using System.Data;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Support.Api.Infrastructure;

public static class SchemaMigrator
{
    private const string MigrationId = "001_initial_schema";

    public static async Task ApplyAsync(SupportDbContext db, CancellationToken cancellationToken = default)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using (var createHistory = connection.CreateCommand())
        {
            createHistory.CommandText = """
                CREATE TABLE IF NOT EXISTS "SchemaMigrations" (
                    "MigrationId" varchar(150) PRIMARY KEY,
                    "AppliedAt" timestamptz NOT NULL
                )
                """;
            await createHistory.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var acquireLock = connection.CreateCommand())
        {
            acquireLock.Transaction = transaction;
            acquireLock.CommandText = "SELECT pg_advisory_xact_lock(74829102)";
            await acquireLock.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = "SELECT EXISTS (SELECT 1 FROM \"SchemaMigrations\" WHERE \"MigrationId\" = @id)";
            var parameter = check.CreateParameter();
            parameter.ParameterName = "id";
            parameter.Value = MigrationId;
            check.Parameters.Add(parameter);
            if (Equals(await check.ExecuteScalarAsync(cancellationToken), true))
            {
                await transaction.CommitAsync(cancellationToken);
                return;
            }
        }

        await using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("Support.Api.Database.001_initial_schema.sql")
            ?? throw new InvalidOperationException("Embedded SQL migration 001_initial_schema.sql was not found.");
        using var reader = new StreamReader(resource);
        var sql = await reader.ReadToEndAsync(cancellationToken);
        await using (var apply = connection.CreateCommand())
        {
            apply.Transaction = transaction;
            apply.CommandText = sql;
            await apply.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var record = connection.CreateCommand())
        {
            record.Transaction = transaction;
            record.CommandText = "INSERT INTO \"SchemaMigrations\" (\"MigrationId\", \"AppliedAt\") VALUES (@id, now())";
            var parameter = record.CreateParameter();
            parameter.ParameterName = "id";
            parameter.Value = MigrationId;
            record.Parameters.Add(parameter);
            await record.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }
}
