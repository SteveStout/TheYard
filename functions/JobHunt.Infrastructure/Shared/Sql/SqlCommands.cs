using System.Data;
using Microsoft.Data.SqlClient;

namespace JobHunt.Infrastructure.Shared.Sql;

/// <summary>
/// The one command builder every SQL store in the Function uses: text, a thirty-second timeout, and a day sent as
/// a `date`. AddWithValue would send a DateTime as the old `datetime`, which starts in 1753 and refuses anything
/// earlier on the client; every column the job hunt keeps a day in is a `date`.
/// </summary>
public static class SqlCommands
{
    /// <summary>A command over the connection with its parameters, a null sent as DBNull.</summary>
    public static SqlCommand Build(SqlConnection? connection, string sql, params (string Name, object? Value)[] parameters)
    {
        var command = new SqlCommand(sql, connection) { CommandType = CommandType.Text, CommandTimeout = 30 };
        foreach (var (name, value) in parameters)
        {
            if (value is DateOnly day)
            {
                command.Parameters.Add(Date(name, day));
            }
            else
            {
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            }
        }
        return command;
    }

    /// <summary>A day as a SQL `date` parameter.</summary>
    public static SqlParameter Date(string name, DateOnly day) => new(name, SqlDbType.Date) { Value = day.ToDateTime(new TimeOnly(0, 0)) };
}
