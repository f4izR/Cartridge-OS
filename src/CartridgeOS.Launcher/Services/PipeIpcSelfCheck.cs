using System.IO;
using CartridgeOS.Core.Data;
using CartridgeOS.Core.Ipc;
using Microsoft.Data.Sqlite;

namespace CartridgeOS.Launcher.Services;

/// <summary>
/// Run via `dotnet run --project src/CartridgeOS.Launcher -- --self-check-ipc`.
/// Exits 0 on pass, 1 on fail. Runs a real pipe server and client in-process (a named
/// pipe works fine within one process) against a throwaway database, to verify the
/// actual framing/connect/request/response round-trip rather than just the DTOs.
/// </summary>
public static class PipeIpcSelfCheck
{
    public static bool Run()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"cartridgeos-selfcheck-{Guid.NewGuid():N}.db");
        try
        {
            var db = new GameDatabase(dbPath);
            db.AddGame(new Core.Models.Game { Title = "Self-Check Game", ExecutablePath = string.Empty });

            var server = new CartridgeOsPipeServer(request => request.Command switch
            {
                "Ping" => new PipeResponse(true, "Pong"),
                "GetGameCount" => new PipeResponse(true, db.GetAllGames().Count.ToString()),
                _ => new PipeResponse(false, "unknown command"),
            });

            using var cts = new CancellationTokenSource();
            var serverTask = server.RunAsync(cts.Token);

            var client = new CartridgeOsPipeClient();

            var pingResponse = client.SendAsync(new PipeRequest("Ping")).GetAwaiter().GetResult();
            if (pingResponse is not { Success: true, Payload: "Pong" }) return false;

            var countResponse = client.SendAsync(new PipeRequest("GetGameCount")).GetAwaiter().GetResult();
            if (countResponse is not { Success: true, Payload: "1" }) return false;

            var unknownResponse = client.SendAsync(new PipeRequest("NotARealCommand")).GetAwaiter().GetResult();
            if (unknownResponse is not { Success: false }) return false;

            cts.Cancel();
            try { serverTask.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }

            return true;
        }
        finally
        {
            // Microsoft.Data.Sqlite pools connections by default — Dispose() doesn't actually
            // release the file handle, so the delete below would fail without this.
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}
