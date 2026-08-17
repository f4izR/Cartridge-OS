using CartridgeOS.Launcher.Services;

namespace CartridgeOS.Tests;

/// <summary>
/// Thin xUnit wrappers around the *SelfCheck classes in CartridgeOS.Launcher/Services — each one's
/// Run() method is the actual check logic (already exercised for months via `dotnet run -- --self-check-*`,
/// see README), unchanged here. This just makes them discoverable/runnable by `dotnet test` and CI instead
/// of requiring someone to remember and run each `--self-check-*` flag by hand.
/// </summary>
public class SelfCheckTests
{
    [Fact]
    public void Artwork() => Assert.True(ArtworkCacheSelfCheck.Run());

    [Fact]
    public void Steam() => Assert.True(SteamScannerSelfCheck.Run());

    [Fact]
    public void Epic() => Assert.True(EpicManifestSelfCheck.Run());

    [Fact]
    public void Riot() => Assert.True(RiotManifestSelfCheck.Run());

    [Fact]
    public void ExecutableHeuristics() => Assert.True(ExecutableHeuristicsSelfCheck.Run());

    [Fact]
    public void Standalone() => Assert.True(StandaloneScannerSelfCheck.Run());

    [Fact]
    public void Sound() => Assert.True(SoundServiceSelfCheck.Run());

    [Fact]
    public void Ipc() => Assert.True(PipeIpcSelfCheck.Run());

    [Fact]
    public void MouseEmulation() => Assert.True(MouseEmulationSelfCheck.Run());

    [Fact]
    public void Xbox() => Assert.True(XboxScannerSelfCheck.Run());
}
