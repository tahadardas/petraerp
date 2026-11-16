using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace PetraERP.Infrastructure.Diagnostics;

/// <summary>Adds the execution time of every EF Core command to the active <see cref="RequestMetrics"/> scope.</summary>
public sealed class SqlTimingInterceptor : DbCommandInterceptor
{
    private SqlTimingInterceptor()
    {
    }

    /// <summary>Stateless, thread-safe shared instance.</summary>
    public static SqlTimingInterceptor Instance { get; } = new();

    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        Record(eventData);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
        DbDataReader result, CancellationToken cancellationToken = default)
    {
        Record(eventData);
        return ValueTask.FromResult(result);
    }

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        Record(eventData);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        Record(eventData);
        return ValueTask.FromResult(result);
    }

    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        Record(eventData);
        return result;
    }

    public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object? result,
        CancellationToken cancellationToken = default)
    {
        Record(eventData);
        return ValueTask.FromResult(result);
    }

    private static void Record(CommandExecutedEventData eventData) => RequestMetrics.Current?.AddSql(eventData.Duration);
}
