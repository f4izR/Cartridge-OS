using System.Text;

namespace CartridgeOS.Core.Ipc;

/// <summary>Length-prefixed UTF-8 message framing shared by the pipe server and client.</summary>
internal static class PipeFraming
{
    private const int MaxMessageBytes = 10 * 1024 * 1024; // sanity cap against a corrupt/malicious length prefix

    public static async Task WriteMessageAsync(Stream stream, string json, CancellationToken ct = default)
    {
        byte[] payload = Encoding.UTF8.GetBytes(json);
        await stream.WriteAsync(BitConverter.GetBytes(payload.Length), ct).ConfigureAwait(false);
        await stream.WriteAsync(payload, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Returns null if the stream closed before a complete message arrived.</summary>
    public static async Task<string?> ReadMessageAsync(Stream stream, CancellationToken ct = default)
    {
        byte[] lengthBuffer = new byte[4];
        if (await ReadExactAsync(stream, lengthBuffer, ct).ConfigureAwait(false) < lengthBuffer.Length) return null;

        int length = BitConverter.ToInt32(lengthBuffer);
        if (length <= 0 || length > MaxMessageBytes) return null;

        byte[] payload = new byte[length];
        if (await ReadExactAsync(stream, payload, ct).ConfigureAwait(false) < length) return null;

        return Encoding.UTF8.GetString(payload);
    }

    private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(totalRead), ct).ConfigureAwait(false);
            if (read == 0) break; // other end closed the pipe
            totalRead += read;
        }
        return totalRead;
    }
}
