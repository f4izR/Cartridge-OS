using Microsoft.Data.Sqlite;
using System.Security.AccessControl;
using System.Security.Principal;
using CartridgeOS.Core.Models;

namespace CartridgeOS.Core.Data;

public sealed class GameDatabase
{
    private readonly string _connectionString;

    public GameDatabase(string dbPath)
    {
        SecureDataDirectory(Path.GetDirectoryName(dbPath)!);

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
                LastPlayedUtc TEXT,
                TotalPlaytimeMinutes INTEGER NOT NULL DEFAULT 0,
                HeroImagePath TEXT
            );
            """;
        command.ExecuteNonQuery();

        // ponytail: no migration framework in this app (see context.md) — an existing DB from before these
        // columns existed just gets them added once; SQLite has no "ADD COLUMN IF NOT EXISTS", so the
        // duplicate-column error on a DB that already has one is the expected, swallowed case, not a failure.
        AddColumnIfMissing(connection, "TotalPlaytimeMinutes", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "HeroImagePath", "TEXT");
    }

    // This directory also holds settings.json, the artwork cache, and every *.log file — GameDatabase
    // isn't really "the" owner of it, but its constructor runs first in both the Launcher and Service
    // startup paths, so this is the one guaranteed place to harden it before anything else gets written
    // there. Restricts to the current user + Administrators + SYSTEM, dropping whatever ACL the folder
    // would otherwise inherit — relevant on a shared/couch-gaming PC with multiple standard-user Windows
    // accounts. Only affects the directory (and anything created in it afterward); a pre-existing games.db
    // from before this change keeps its old file-level ACL until rewritten. Best-effort: SQLite itself
    // still works if this fails (e.g. a non-NTFS volume), it just means no extra hardening happened.
    private static void SecureDataDirectory(string dir)
    {
        var info = Directory.CreateDirectory(dir);
        try
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            var currentUser = WindowsIdentity.GetCurrent().User;
            if (currentUser is not null) security.AddAccessRule(FullControlRule(currentUser));
            security.AddAccessRule(FullControlRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)));
            security.AddAccessRule(FullControlRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null)));

            info.SetAccessControl(security);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
        {
            // best-effort — leave whatever ACL the directory already has
        }
    }

    private static FileSystemAccessRule FullControlRule(IdentityReference identity) => new(
        identity, FileSystemRights.FullControl,
        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow);

    private static void AddColumnIfMissing(SqliteConnection connection, string columnName, string columnDefinition)
    {
        try
        {
            using var addColumn = connection.CreateCommand();
            addColumn.CommandText = $"ALTER TABLE Games ADD COLUMN {columnName} {columnDefinition};";
            addColumn.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // column already exists
        }
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

    public void UpdateArtworkPath(int id, string? artworkPath)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Games SET ArtworkPath = $artwork WHERE Id = $id;";
        command.Parameters.AddWithValue("$artwork", (object?)artworkPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void UpdateHeroImagePath(int id, string? heroImagePath)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Games SET HeroImagePath = $hero WHERE Id = $id;";
        command.Parameters.AddWithValue("$hero", (object?)heroImagePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>Adds elapsed minutes to a game's running total playtime — called once when the game exits (only reachable for a directly-tracked process, same limitation as the in-game overlay).</summary>
    public void AddPlaytime(int id, int minutes)
    {
        if (minutes <= 0) return;
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Games SET TotalPlaytimeMinutes = TotalPlaytimeMinutes + $minutes WHERE Id = $id;";
        command.Parameters.AddWithValue("$minutes", minutes);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void DeleteGame(int id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Games WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public List<Game> GetAllGames()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Title, ExecutablePath, ArtworkPath, LaunchArgs, LastPlayedUtc, TotalPlaytimeMinutes, HeroImagePath FROM Games ORDER BY Title;";
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
                LastPlayedUtc = reader.IsDBNull(5) ? null : DateTime.Parse(reader.GetString(5)),
                TotalPlaytimeMinutes = reader.GetInt32(6),
                HeroImagePath = reader.IsDBNull(7) ? null : reader.GetString(7)
            });
        }
        return games;
    }
}
