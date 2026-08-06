namespace CartridgeOS.Core.Ipc;

public sealed record PipeRequest(string Command, string? Payload = null);
public sealed record PipeResponse(bool Success, string? Payload = null);
