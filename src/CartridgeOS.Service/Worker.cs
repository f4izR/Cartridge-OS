using CartridgeOS.Core.Data;
using CartridgeOS.Core.Ipc;

namespace CartridgeOS.Service;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CartridgeOS", "games.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        var db = new GameDatabase(dbPath);

        var server = new CartridgeOsPipeServer(request => HandleRequest(request, db));

        _logger.LogInformation("IPC server listening on pipe '{PipeName}'", CartridgeOsPipeServer.DefaultPipeName);
        await server.RunAsync(stoppingToken);
    }

    private static PipeResponse HandleRequest(PipeRequest request, GameDatabase db) => request.Command switch
    {
        "Ping" => new PipeResponse(true, "Pong"),
        "GetGameCount" => new PipeResponse(true, db.GetAllGames().Count.ToString()),
        _ => new PipeResponse(false, $"unknown command '{request.Command}'"),
    };
}
