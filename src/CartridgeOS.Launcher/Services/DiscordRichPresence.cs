using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace CartridgeOS.Launcher.Services;

/// <summary>
/// Discord Rich Presence via Discord's local IPC pipe (\\.\pipe\discord-ipc-{0..9}) — raw
/// implementation of Discord's documented handshake/frame protocol, no discord-rpc dependency
/// needed for just "set/clear activity". Every call is best-effort: Discord may not be running,
/// the pipe may close mid-session, none of that should ever break game launching, so every public
/// method swallows its own errors (same pattern as ArtworkCache/SoundService elsewhere here).
/// </summary>
public sealed class DiscordRichPresence : IDisposable
{
    // Discord Application ID for "Cartridge OS" (developer.discord.com) — not a secret, every install shares it.
    private const string ClientId = "1535179486117502976";

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CartridgeOS", "discord.log");

    private NamedPipeClientStream? _pipe;
    private bool _ready;
    private readonly DateTimeOffset _appStartedAtUtc = DateTimeOffset.UtcNow;

    public async Task ConnectAsync()
    {
        for (int i = 0; i < 10 && _pipe is null; i++)
        {
            try
            {
                var pipe = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(300).ConfigureAwait(false);
                _pipe = pipe;
                Log($"connected to discord-ipc-{i}");
            }
            catch (Exception ex) when (ex is IOException or TimeoutException)
            {
                // that pipe number isn't Discord — try the next one
            }
        }
        if (_pipe is null)
        {
            Log("no discord-ipc-N pipe found (checked 0..9) — Discord isn't running, or its IPC pipe wasn't reachable");
            return;
        }

        try
        {
            await SendAsync(0, new { v = 1, client_id = ClientId }).ConfigureAwait(false);
            string response = await ReadFrameAsync().ConfigureAwait(false);
            Log($"handshake response: {response}");

            // Discord replies even to a bad client_id — it just sends evt:"ERROR" instead of failing the
            // pipe connect, so the old code (which never looked at the response) treated that as success.
            using var doc = JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty("evt", out var evtProp) && evtProp.GetString() == "ERROR")
            {
                string message = doc.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("message", out var msg)
                    ? msg.GetString() ?? "(no message)" : "(no message)";
                Log($"handshake rejected by Discord — {message}. Check that ClientId '{ClientId}' is still a valid registered application.");
                return; // _ready stays false — every SetActivityAsync call from here on is a safe no-op
            }

            _ready = true;
            // Without this, Discord shows nothing at all until a game is actually launched (and reverts to
            // nothing the moment it exits) — the app itself never had any presence of its own.
            await SetIdleActivityAsync().ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            Log($"handshake failed — {ex.Message}");
            _pipe = null;
        }
    }

    // Key name of the uploaded Rich Presence art asset (Discord Developer Portal → Rich Presence → Art
    // Assets) — shows as a question mark in Discord until an asset with this exact key exists there; this
    // app can't upload it itself, that's a manual step tied to the portal login, not something IPC can do.
    private const string LargeImageKey = "logo";

    public Task SetActivityAsync(string gameTitle, DateTimeOffset startedAt) => SendActivityAsync(gameTitle, new
    {
        details = gameTitle,
        state = "Playing",
        timestamps = new { start = startedAt.ToUnixTimeSeconds() },
        assets = new { large_image = LargeImageKey, large_text = "Cartridge OS" },
    });

    /// <summary>Shown whenever no game is running — on connect, and again once a game exits — so Discord
    /// always shows *something* for as long as Cartridge OS is open, not just mid-session. Timestamp is the
    /// app's own launch time, not the current moment, so the "elapsed" counter reflects how long the user's
    /// actually been in Cartridge OS rather than resetting to 0:00 every time a game closes.</summary>
    public Task SetIdleActivityAsync() => SendActivityAsync("(idle)", new
    {
        details = "Browsing the library",
        state = "In Cartridge OS",
        timestamps = new { start = _appStartedAtUtc.ToUnixTimeSeconds() },
        assets = new { large_image = LargeImageKey, large_text = "Cartridge OS" },
    });

    private async Task SendActivityAsync(string? gameTitle, object? activity)
    {
        if (!_ready || _pipe is null)
        {
            Log($"SET_ACTIVITY('{gameTitle}') skipped — not connected to Discord");
            return;
        }
        try
        {
            await SendAsync(1, new
            {
                cmd = "SET_ACTIVITY",
                args = new { pid = Environment.ProcessId, activity },
                nonce = Guid.NewGuid().ToString(),
            }).ConfigureAwait(false);

            // Discord replies to every command with a response frame (echo on success, evt:"ERROR" on
            // failure) — the old code never read it, so a rejected activity update was invisible.
            string response = await ReadFrameAsync().ConfigureAwait(false);
            Log($"SET_ACTIVITY('{gameTitle}') response: {response}");
        }
        catch (IOException ex)
        {
            Log($"SET_ACTIVITY('{gameTitle}') failed — {ex.Message}");
            _ready = false; // Discord closed the pipe (quit/restarted) — stop trying until next ConnectAsync
        }
    }

    private async Task SendAsync(int opcode, object payload)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(payload);
        byte[] header = new byte[8];
        BitConverter.GetBytes(opcode).CopyTo(header, 0);
        BitConverter.GetBytes(json.Length).CopyTo(header, 4);
        await _pipe!.WriteAsync(header).ConfigureAwait(false);
        await _pipe!.WriteAsync(json).ConfigureAwait(false);
        await _pipe!.FlushAsync().ConfigureAwait(false);
    }

    private async Task<string> ReadFrameAsync()
    {
        byte[] header = new byte[8];
        await _pipe!.ReadExactlyAsync(header).ConfigureAwait(false);
        int length = BitConverter.ToInt32(header, 4);
        byte[] body = new byte[length];
        await _pipe!.ReadExactlyAsync(body).ConfigureAwait(false);
        return Encoding.UTF8.GetString(body);
    }

    // ponytail: plain append-to-file log, no rotation — this file stays tiny (a few lines per app session).
    private static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch (IOException) { }
    }

    public void Dispose() => _pipe?.Dispose();
}
