using Microsoft.Data.Sqlite;

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

    internal int ScalarInt(string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
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

    internal void EnsureIntegrity()
    {
        var result = ScalarText("pragma integrity_check");
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("SQLite integrity_check failed: " + result);
        }
    }

    public void Dispose() => connection.Dispose();
}
