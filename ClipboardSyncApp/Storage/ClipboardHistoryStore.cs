using Microsoft.Data.Sqlite;

namespace ClipboardSyncApp.Storage;

public sealed class ClipboardHistoryStore
{
    private readonly string _connectionString;

    public ClipboardHistoryStore(string? dbPath = null)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClipboardSync");
        Directory.CreateDirectory(folder);
        _connectionString = dbPath ?? Path.Combine(folder, "history.db");
        EnsureSchema();
    }

    public void AddEntry(string preview, string kind, string source)
    {
        using var connection = new SqliteConnection($"Data Source={_connectionString}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS History (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Timestamp TEXT NOT NULL,
                Preview TEXT NOT NULL,
                Kind TEXT NOT NULL,
                Source TEXT NOT NULL
            );";
        command.ExecuteNonQuery();

        command.CommandText = @"
            INSERT INTO History (Timestamp, Preview, Kind, Source)
            VALUES (@ts, @preview, @kind, @source);";
        command.Parameters.AddWithValue("@ts", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("@preview", preview);
        command.Parameters.AddWithValue("@kind", kind);
        command.Parameters.AddWithValue("@source", source);
        command.ExecuteNonQuery();
    }

    public List<(DateTime Timestamp, string Preview, string Kind, string Source)> GetRecent(int maxItems = 200)
    {
        using var connection = new SqliteConnection($"Data Source={_connectionString}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Timestamp, Preview, Kind, Source
            FROM History
            ORDER BY Id DESC
            LIMIT @limit;";
        command.Parameters.AddWithValue("@limit", maxItems);

        var results = new List<(DateTime, string, string, string)>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add((
                DateTime.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)));
        }

        return results;
    }

    private void EnsureSchema()
    {
        using var connection = new SqliteConnection($"Data Source={_connectionString}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS History (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Timestamp TEXT NOT NULL,
                Preview TEXT NOT NULL,
                Kind TEXT NOT NULL,
                Source TEXT NOT NULL
            );";
        command.ExecuteNonQuery();
    }
}
