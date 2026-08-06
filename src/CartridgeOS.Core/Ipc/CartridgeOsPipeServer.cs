using System.IO.Pipes;
using System.Text.Json;

namespace CartridgeOS.Core.Ipc;

/// <summary>
/// Minimal named-pipe request/response server: each connection handles exactly one
/// request then closes, so callers don't need to manage a long-lived duplex session.
/// One well-known pipe name shared by every CartridgeOS process.
/// </summary>
public sealed class CartridgeOsPipeServer(Func<PipeRequest, PipeResponse> handleRequest)
{
    public const string PipeName = "CartridgeOS.IPC";

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            try
            {
                await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);

                string? json = await PipeFraming.ReadMessageAsync(pipe, ct).ConfigureAwait(false);
                if (json is null) continue;

                var response = TryHandle(json);
                await PipeFraming.WriteMessageAsync(pipe, JsonSerializer.Serialize(response), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException)
            {
                // client disconnected mid-message — drop this connection, keep serving the next one
            }
        }
    }

    private PipeResponse TryHandle(string requestJson)
    {
        try
        {
            var request = JsonSerializer.Deserialize<PipeRequest>(requestJson);
            return request is null
                ? new PipeResponse(false, "malformed request")
                : handleRequest(request);
        }
        catch (Exception ex)
        {
            return new PipeResponse(false, ex.Message);
        }
    }
}
