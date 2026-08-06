using System.Linq;
using System.Windows;
using CartridgeOS.Launcher.Services;

namespace CartridgeOS.Launcher;

public partial class App : Application
{
    private static readonly Dictionary<string, Func<bool>> SelfChecks = new()
    {
        ["--self-check-artwork"] = ArtworkCacheSelfCheck.Run,
        ["--self-check-steam"] = SteamScannerSelfCheck.Run,
        ["--self-check-epic"] = EpicManifestSelfCheck.Run,
        ["--self-check-riot"] = RiotManifestSelfCheck.Run,
        ["--self-check-executable-heuristics"] = ExecutableHeuristicsSelfCheck.Run,
        ["--self-check-standalone"] = StandaloneScannerSelfCheck.Run,
        ["--self-check-sound"] = SoundServiceSelfCheck.Run,
        ["--self-check-ipc"] = PipeIpcSelfCheck.Run,
        ["--self-check-mouse-emulation"] = MouseEmulationSelfCheck.Run,
        ["--self-check-xbox"] = XboxScannerSelfCheck.Run,
    };

    protected override void OnStartup(StartupEventArgs e)
    {
        var selfCheck = SelfChecks.Keys.FirstOrDefault(e.Args.Contains);
        if (selfCheck is not null)
        {
            Environment.Exit(SelfChecks[selfCheck]() ? 0 : 1);
            return;
        }

        // Cross-process diagnostic (separate from --self-check-ipc, which runs its own in-process
        // server): pings whatever is actually listening on the pipe, e.g. a real running Service.
        if (e.Args.Contains("--ipc-ping"))
        {
            var response = new CartridgeOS.Core.Ipc.CartridgeOsPipeClient()
                .SendAsync(new CartridgeOS.Core.Ipc.PipeRequest("GetGameCount"))
                .GetAwaiter().GetResult();
            Environment.Exit(response is { Success: true } ? 0 : 1);
            return;
        }

        base.OnStartup(e);
    }
}
