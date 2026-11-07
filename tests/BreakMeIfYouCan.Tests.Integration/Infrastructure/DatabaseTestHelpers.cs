using System.Data.Common;
using BreakMeIfYouCan.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BreakMeIfYouCan.Tests.Integration.Infrastructure;

public static class DatabaseTestHelpers
{
    public static ArenaDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<ArenaDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "arena"))
            .Options);

    public static async Task<long> ScalarLongAsync(string connectionString, string sql, params (string Name, object Value)[] parameters)
    {
        await using var context = CreateContext(connectionString);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameters(command, parameters);
        var value = await command.ExecuteScalarAsync();
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    public static async Task ExecuteAsync(string connectionString, string sql, params (string Name, object Value)[] parameters)
    {
        await using var context = CreateContext(connectionString);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameters(command, parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static void AddParameters(DbCommand command, (string Name, object Value)[] parameters)
    {
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }
    }
}
