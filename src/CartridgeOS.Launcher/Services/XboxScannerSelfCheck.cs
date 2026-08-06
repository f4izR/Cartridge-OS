using CartridgeOS.Core.Scanning;

namespace CartridgeOS.Launcher.Services;

/// <summary>
/// Run via `dotnet run --project src/CartridgeOS.Launcher -- --self-check-xbox`.
/// Exits 0 on pass, 1 on fail. Verifies JSON parsing against sample PowerShell output
/// (both the array and bare-object shapes ConvertTo-Json can produce), then actually runs
/// the real PowerShell script as a smoke test — no Xbox/Store games are installed on this
/// dev machine, so this only confirms the script itself doesn't error, not that it finds
/// anything real.
/// </summary>
public static class XboxScannerSelfCheck
{
    private const string SampleArrayJson = """
        [
            {"Title": "Sea of Thieves", "PackageFamilyName": "MicrosoftStudios.SeaofThieves_8wekyb3d8bbwe", "AppId": "SeaOfThieves"},
            {"Title": "Forza Horizon 5", "PackageFamilyName": "Microsoft.ForzaHorizon5_8wekyb3d8bbwe", "AppId": "App"}
        ]
        """;

    private const string SampleSingleObjectJson =
        """{"Title": "Halo Infinite", "PackageFamilyName": "Microsoft.HaloInfinite_8wekyb3d8bbwe", "AppId": "App"}""";

    public static bool Run()
    {
        var multi = XboxScanner.Parse(SampleArrayJson);
        if (multi.Count != 2) return false;
        if (multi[0].Title != "Sea of Thieves") return false;
        if (multi[0].ExecutablePath != @"shell:appsFolder\MicrosoftStudios.SeaofThieves_8wekyb3d8bbwe!SeaOfThieves") return false;

        var single = XboxScanner.Parse(SampleSingleObjectJson);
        if (single.Count != 1) return false;
        if (single[0].Title != "Halo Infinite") return false;

        if (XboxScanner.Parse("").Count != 0) return false;
        if (XboxScanner.Parse("not json").Count != 0) return false;
        if (XboxScanner.Parse("""{"Title": "missing fields"}""").Count != 0) return false;

        try
        {
            _ = new XboxScanner().Scan(); // real smoke test — catches a broken PowerShell script even with nothing installed to find
        }
        catch (Exception)
        {
            return false;
        }

        return true;
    }
}
