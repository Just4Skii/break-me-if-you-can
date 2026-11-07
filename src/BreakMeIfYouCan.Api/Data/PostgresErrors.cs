using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BreakMeIfYouCan.Api.Data;

public static class PostgresErrors
{
    public static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
