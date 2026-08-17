using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace CartridgeOS.Core.Ipc;

/// <summary>
/// Minimal named-pipe request/response server: each connection handles exactly one
/// request then closes, so callers don't need to manage a long-lived duplex session.
/// One well-known pipe name shared by every CartridgeOS process.
/// </summary>
public sealed class CartridgeOsPipeServer(Func<PipeRequest, PipeResponse> handleRequest, string pipeName = CartridgeOsPipeServer.DefaultPipeName)
{
    public const string DefaultPipeName = "CartridgeOS.IPC";

    // Every command this pipe accepts is harmless today (Ping/GetGameCount/ShowLauncher), but the pipe
    // has no default ACL restriction otherwise — anonymous/guest logons on the same machine could
    // connect and issue commands to whatever the Service ends up trusting later, including once the
    // Service runs as LocalSystem. Restricting to Authenticated Users (excludes Anonymous/Guest) is the
    // standard hardening for a local Windows named pipe; still local-machine only either way, since
    // this is always opened against server name ".".
    private static PipeSecurity BuildPipeSecurity()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));
        return security;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var pipe = NamedPipeServerStreamAcl.Create(
                pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
                inBufferSize: 0, outBufferSize: 0, pipeSecurity: BuildPipeSecurity());
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

    // Every real command today is a short fixed string ("Ping", "GetGameCount", "ShowLauncher") with no
    // Payload — PipeFraming's 10MB cap is sized for message framing safety, not for what a well-formed
    // request actually needs. Reject anything wildly outside that shape before it ever reaches
    // handleRequest, rather than trusting whatever the other end of the pipe sent.
    private const int MaxCommandLength = 256;
    private const int MaxPayloadLength = 4096;

    private PipeResponse TryHandle(string requestJson)
    {
        try
        {
            var request = JsonSerializer.Deserialize<PipeRequest>(requestJson);
            if (request is null) return new PipeResponse(false, "malformed request");
            if (string.IsNullOrWhiteSpace(request.Command) || request.Command.Length > MaxCommandLength)
                return new PipeResponse(false, "invalid command");
            if (request.Payload is { Length: > MaxPayloadLength })
                return new PipeResponse(false, "payload too large");

            return handleRequest(request);
        }
        catch (Exception ex)
        {
            return new PipeResponse(false, ex.Message);
        }
    }
}
