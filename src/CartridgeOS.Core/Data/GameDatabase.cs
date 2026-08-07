using Microsoft.Data.Sqlite;
using CartridgeOS.Core.Models;

namespace CartridgeOS.Core.Data;

public sealed class GameDatabase
{
    private readonly string _connectionString;

    public GameDatabase(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Games (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL,
                ExecutablePath TEXT NOT NULL,
                ArtworkPath TEXT,
                LaunchArgs TEXT,
                LastPlayedUtc TEXT
            );
            """;
        command.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    public int AddGame(Game game)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Games (Title, ExecutablePath, ArtworkPath, LaunchArgs, LastPlayedUtc)
            VALUES ($title, $exePath, $artwork, $launchArgs, $lastPlayed);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$title", game.Title);
        command.Parameters.AddWithValue("$exePath", game.ExecutablePath);
        command.Parameters.AddWithValue("$artwork", (object?)game.ArtworkPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$launchArgs", (object?)game.LaunchArgs ?? DBNull.Value);
        command.Parameters.AddWithValue("$lastPlayed", (object?)game.LastPlayedUtc?.ToString("O") ?? DBNull.Value);
        return Convert.ToInt32((long)command.ExecuteScalar()!);
    }

    public void UpdateLastPlayed(int id, DateTime lastPlayedUtc)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Games SET LastPlayedUtc = $lastPlayed WHERE Id = $id;";
        command.Parameters.AddWithValue("$lastPlayed", lastPlayedUtc.ToString("O"));
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void UpdateArtworkPath(int id, string artworkPath)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Games SET ArtworkPath = $artwork WHERE Id = $id;";
        command.Parameters.AddWithValue("$artwork", artworkPath);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public List<Game> GetAllGames()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Title, ExecutablePath, ArtworkPath, LaunchArgs, LastPlayedUtc FROM Games ORDER BY Title;";
        using var reader = command.ExecuteReader();

        var games = new List<Game>();
        while (reader.Read())
        {
            games.Add(new Game
            {
                Id = reader.GetInt32(0),
                Title = reader.GetString(1),
                ExecutablePath = reader.GetString(2),
                ArtworkPath = reader.IsDBNull(3) ? null : reader.GetString(3),
                LaunchArgs = reader.IsDBNull(4) ? null : reader.GetString(4),
                LastPlayedUtc = reader.IsDBNull(5) ? null : DateTime.Parse(reader.GetString(5))
            });
        }
        return games;
    }
}
