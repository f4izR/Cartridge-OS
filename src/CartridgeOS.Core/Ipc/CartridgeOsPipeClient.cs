using System.IO.Pipes;
using System.Text.Json;

namespace CartridgeOS.Core.Ipc;

public sealed class CartridgeOsPipeClient
{
    /// <summary>Returns null if nothing's listening on the pipe, it's busy, or the request timed out — never throws.</summary>
    public async Task<PipeResponse?> SendAsync(PipeRequest request, string pipeName = CartridgeOsPipeServer.DefaultPipeName, int timeoutMs = 2000)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var cts = new CancellationTokenSource(timeoutMs);

            await pipe.ConnectAsync(timeoutMs, cts.Token).ConfigureAwait(false);
            await PipeFraming.WriteMessageAsync(pipe, JsonSerializer.Serialize(request), cts.Token).ConfigureAwait(false);

            string? json = await PipeFraming.ReadMessageAsync(pipe, cts.Token).ConfigureAwait(false);
            return json is null ? null : JsonSerializer.Deserialize<PipeResponse>(json);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
