using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TheYard.Application;

namespace TheYard.Infrastructure;

// #region sql-interceptor
/// <summary>
/// Reads every command EF Core sends and hands it to the <see cref="ISqlLog"/>.
///
/// This is an interceptor rather than a log filter on purpose. EF's own
/// "Executed DbCommand" log line already contains everything, including the
/// parameter values, as one formatted string. Parsing that string to take the
/// values back out would mean the values had already been built, already been
/// in memory as a message, and already been handed to every other logging
/// provider in the process, Application Insights included. The interceptor sees
/// the <see cref="DbCommand"/> itself, so this code can read a parameter's name,
/// type and size and never touch <c>Value</c> at all.
/// </summary>
public sealed class SqlLogInterceptor(ISqlLog log, ICurrentRequest request) : DbCommandInterceptor
{
    // A statement is trimmed before it is stored: EF writes a SELECT of every
    // column of Vehicles, which is thirty of them, and the Admin tab is a page
    // rather than a query editor.
    private const int MaxTextLength = 2_000;

    private void Record(DbCommand command, TimeSpan duration, string outcome)
    {
        // Nothing in here is allowed to break a query that worked.
        //
        // This runs on the path of every command the application sends. A
        // provider whose DbType getter throws for some exotic parameter, or a
        // ring that is somehow in a bad state, would otherwise turn a healthy
        // SELECT into a failed request, and the Admin tab would have caused the
        // outage it exists to explain. An observability hook that can break the
        // thing it observes is worse than no hook (the staff review,
        // 2026-09-03).
        try
        {
            RecordOrThrow(command, duration, outcome);
        }
        catch
        {
            // Deliberately silent, and deliberately not logged: this is called
            // from inside a database command, and a logger here is one more
            // thing that can fail on the same path.
        }
    }

    private void RecordOrThrow(DbCommand command, TimeSpan duration, string outcome)
    {
        var parameters = new List<SqlParameterShape>(command.Parameters.Count);
        foreach (DbParameter parameter in command.Parameters)
        {
            // Name, type, size. Never Value. See the comment on SqlStatement.
            parameters.Add(new SqlParameterShape(
                parameter.ParameterName,
                parameter.DbType.ToString(),
                parameter.Size == 0 ? null : parameter.Size));
        }

        string text = command.CommandText.Length <= MaxTextLength
            ? command.CommandText
            : command.CommandText[..MaxTextLength] + "\n... trimmed";

        log.Record(new SqlStatement(
            DateTimeOffset.UtcNow,
            text,
            parameters,
            (long)duration.TotalMilliseconds,
            outcome,
            request.Describe()));
    }

    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        Record(command, eventData.Duration, "read");
        return base.ReaderExecuted(command, eventData, result);
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        Record(command, eventData.Duration, "read");
        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        Record(command, eventData.Duration, $"{result} row(s) affected");
        return base.NonQueryExecuted(command, eventData, result);
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Record(command, eventData.Duration, $"{result} row(s) affected");
        return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        Record(command, eventData.Duration, "scalar");
        return base.ScalarExecuted(command, eventData, result);
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
    {
        Record(command, eventData.Duration, "scalar");
        return base.ScalarExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
    {
        // The exception type, not its message: a provider's message can quote
        // the value that broke the constraint.
        Record(command, eventData.Duration, "failed: " + eventData.Exception.GetType().Name);
        base.CommandFailed(command, eventData);
    }

    public override Task CommandFailedAsync(
        DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Record(command, eventData.Duration, "failed: " + eventData.Exception.GetType().Name);
        return base.CommandFailedAsync(command, eventData, cancellationToken);
    }
}
// #endregion sql-interceptor
