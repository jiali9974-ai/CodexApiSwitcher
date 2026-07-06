using Microsoft.Data.Sqlite;
using System.Data;

namespace CodexApiSwitcher.Core;

internal sealed class SqliteDatabase : IDisposable
{
    private readonly SqliteConnection connection;

    private SqliteDatabase(SqliteConnection sqliteConnection) => connection = sqliteConnection;

    internal static SqliteDatabase Open(string path)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            DefaultTimeout = 30
        };
        var connection = new SqliteConnection(builder.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "pragma busy_timeout = 30000";
        command.ExecuteNonQuery();
        return new SqliteDatabase(connection);
    }

    internal static void Backup(string sourcePath, string destinationPath)
    {
        if (File.Exists(destinationPath)) throw new IOException("Backup destination already exists: " + destinationPath);
        using var source = Open(sourcePath);
        using var destination = Open(destinationPath);
        source.connection.BackupDatabase(destination.connection);
        destination.EnsureIntegrity();
    }

    internal void Execute(string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    internal int Execute(string sql, IReadOnlyDictionary<string, object?> parameters)
    {
        using var command = CreateCommand(sql, parameters);
        return command.ExecuteNonQuery();
    }

    internal int ScalarInt(string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(command.ExecuteScalar());
    }

    internal int ScalarInt(string sql, IReadOnlyDictionary<string, object?> parameters)
    {
        using var command = CreateCommand(sql, parameters);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    internal string ScalarText(string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar()) ?? string.Empty;
    }

    internal List<string> QueryTextColumn(string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read()) values.Add(reader.IsDBNull(0) ? string.Empty : reader.GetString(0));
        return values;
    }

    internal List<Dictionary<string, object?>> QueryRows(string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return ReadRows(command);
    }

    internal List<Dictionary<string, object?>> QueryRows(string sql, IReadOnlyDictionary<string, object?> parameters)
    {
        using var command = CreateCommand(sql, parameters);
        return ReadRows(command);
    }

    internal List<string> GetTableColumns(string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "select name from pragma_table_info(" + QuoteLiteral(table) + ") order by cid";
        using var reader = command.ExecuteReader();
        var columns = new List<string>();
        while (reader.Read()) columns.Add(reader.GetString(0));
        return columns;
    }

    internal void EnsureIntegrity()
    {
        var result = ScalarText("pragma integrity_check");
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("SQLite integrity_check failed: " + result);
        }
    }

    public void Dispose() => connection.Dispose();

    private SqliteCommand CreateCommand(string sql, IReadOnlyDictionary<string, object?> parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (key, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = key.StartsWith('@') ? key : "@" + key;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
        return command;
    }

    private static List<Dictionary<string, object?>> ReadRows(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        var rows = new List<Dictionary<string, object?>>();
        while (reader.Read())
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < reader.FieldCount; index++)
            {
                row[reader.GetName(index)] = reader.IsDBNull(index) ? null : reader.GetValue(index);
            }
            rows.Add(row);
        }
        return rows;
    }

    private static string QuoteLiteral(string value) => "'" + value.Replace("'", "''") + "'";
}
