using System.Data;
using System.Globalization;
using BreakMeIfYouCan.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BreakMeIfYouCan.Api.Arena;

/// <summary>
/// Independent integrity checks straight from PostgreSQL. The numbers in the UI come from
/// these queries, not from the transfer code's own accounting.
/// </summary>
public sealed class LedgerIntegrityService
{
    private const string NetSql = """
        SELECT COALESCE(SUM(CASE WHEN e.direction = 'debit' THEN -e.amount_cents ELSE e.amount_cents END), 0)
        FROM arena.ledger_entries e
        JOIN arena.transfers t ON t.id = e.transfer_id
        WHERE t.sandbox_id = @sandbox AND t.status = 'completed'
        """;

    private const string MalformedSql = """
        SELECT COUNT(*) FROM (
            SELECT t.id
            FROM arena.transfers t
            LEFT JOIN arena.ledger_entries e ON e.transfer_id = t.id
            WHERE t.sandbox_id = @sandbox AND t.status = 'completed'
            GROUP BY t.id
            HAVING COUNT(e.id) <> 2
                OR COALESCE(SUM(CASE WHEN e.direction = 'debit' THEN -e.amount_cents ELSE e.amount_cents END), 0) <> 0
        ) AS malformed
        """;

    private const string NegativeSql = """
        SELECT COUNT(*) FROM arena.accounts
        WHERE sandbox_id = @sandbox AND balance_cents < 0
        """;

    private const string TotalsSql = """
        SELECT
            (SELECT COUNT(*) FROM arena.transfers WHERE sandbox_id = @sandbox AND status = 'completed'),
            (SELECT COUNT(*) FROM arena.ledger_entries e JOIN arena.transfers t ON t.id = e.transfer_id WHERE t.sandbox_id = @sandbox),
            (SELECT COALESCE(SUM(balance_cents), 0) FROM arena.accounts WHERE sandbox_id = @sandbox)
        """;

    public async Task<LedgerIntegrityReport> CheckAsync(ArenaDbContext db, Guid sandboxId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        var net = await ScalarAsync<long>(db, NetSql, sandboxId, cancellationToken);
        var malformed = await ScalarAsync<int>(db, MalformedSql, sandboxId, cancellationToken);
        var negative = await ScalarAsync<int>(db, NegativeSql, sandboxId, cancellationToken);

        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        int completed;
        int entries;
        long total;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = TotalsSql;
            AddSandboxParameter(command, sandboxId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("The ledger totals query returned no row.");
            }

            completed = reader.GetInt32(0);
            entries = reader.GetInt32(1);
            total = reader.GetInt64(2);
        }

        return new LedgerIntegrityReport
        {
            Balanced = net == 0,
            NetCents = net,
            CompletedTransfers = completed,
            LedgerEntries = entries,
            MalformedTransfers = malformed,
            NegativeAccounts = negative,
            TotalBalanceCents = total,
        };
    }

    private static async Task<T> ScalarAsync<T>(ArenaDbContext db, string sql, Guid sandboxId, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddSandboxParameter(command, sandboxId);
        var value = await command.ExecuteScalarAsync(cancellationToken);

        if (value is null or DBNull)
        {
            return default!;
        }

        return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
    }

    private static void AddSandboxParameter(System.Data.Common.DbCommand command, Guid sandboxId)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = "sandbox";
        parameter.Value = sandboxId;
        command.Parameters.Add(parameter);
    }
}
