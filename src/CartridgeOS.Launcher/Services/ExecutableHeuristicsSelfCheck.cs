using System.IO;
using CartridgeOS.Core.Scanning;

namespace CartridgeOS.Launcher.Services;

/// <summary>
/// Run via `dotnet run --project src/CartridgeOS.Launcher -- --self-check-executable-heuristics`.
/// Exits 0 on pass, 1 on fail. Builds a real temp directory of fake exes and checks the
/// picking logic end to end — this one doesn't need a real game install to test honestly.
/// </summary>
public static class ExecutableHeuristicsSelfCheck
{
    public static bool Run()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"cartridgeos-selfcheck-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            WriteFile(dir, "unins000.exe", 500_000);   // installer artifact, should be ignored
            WriteFile(dir, "CrashHandler.exe", 200_000); // small helper, should lose to the real game exe
            WriteFile(dir, "MyGame.exe", 50_000_000);    // the actual game — biggest non-ignored exe

            string? picked = ExecutableHeuristics.FindLikelyGameExecutable(dir);
            if (picked is null) return false;
            if (Path.GetFileName(picked) != "MyGame.exe") return false;

            if (ExecutableHeuristics.FindLikelyGameExecutable(Path.Combine(dir, "does-not-exist")) is not null) return false;

            string emptyDir = Path.Combine(dir, "empty");
            Directory.CreateDirectory(emptyDir);
            if (ExecutableHeuristics.FindLikelyGameExecutable(emptyDir) is not null) return false;

            return true;
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static void WriteFile(string dir, string name, int sizeBytes) =>
        File.WriteAllBytes(Path.Combine(dir, name), new byte[sizeBytes]);
}
