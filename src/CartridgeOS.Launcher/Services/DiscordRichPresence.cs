using System.IO;
using System.IO.Pipes;
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

    private NamedPipeClientStream? _pipe;
    private bool _ready;

    public async Task ConnectAsync()
    {
        for (int i = 0; i < 10 && _pipe is null; i++)
        {
            try
            {
                var pipe = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(300).ConfigureAwait(false);
                _pipe = pipe;
            }
            catch (Exception ex) when (ex is IOException or TimeoutException)
            {
                // that pipe number isn't Discord — try the next one
            }
        }
        if (_pipe is null) return; // Discord isn't running — no-op for the rest of this session

        try
        {
            await SendAsync(0, new { v = 1, client_id = ClientId }).ConfigureAwait(false);
            await ReadFrameAsync().ConfigureAwait(false); // READY event, contents unused — just drain it
            _ready = true;
        }
        catch (IOException)
        {
            _pipe = null;
        }
    }

    public Task SetActivityAsync(string gameTitle, DateTimeOffset startedAt) => SendActivityAsync(new
    {
        details = gameTitle,
        state = "Playing",
        timestamps = new { start = startedAt.ToUnixTimeSeconds() },
    });

    public Task ClearActivityAsync() => SendActivityAsync(null);

    private async Task SendActivityAsync(object? activity)
    {
        if (!_ready || _pipe is null) return;
        try
        {
            await SendAsync(1, new
            {
                cmd = "SET_ACTIVITY",
                args = new { pid = Environment.ProcessId, activity },
                nonce = Guid.NewGuid().ToString(),
            }).ConfigureAwait(false);
        }
        catch (IOException)
        {
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

    private async Task ReadFrameAsync()
    {
        byte[] header = new byte[8];
        await _pipe!.ReadExactlyAsync(header).ConfigureAwait(false);
        int length = BitConverter.ToInt32(header, 4);
        await _pipe!.ReadExactlyAsync(new byte[length]).ConfigureAwait(false);
    }

    public void Dispose() => _pipe?.Dispose();
}
