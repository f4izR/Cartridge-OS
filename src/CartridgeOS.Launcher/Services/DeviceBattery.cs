using System.Runtime.InteropServices;

namespace CartridgeOS.Launcher.Services;

/// <summary>This machine's own battery (laptop) — fallback for the status bar when no controller battery is reported.</summary>
internal static class DeviceBattery
{
    private const byte NoSystemBattery = 128; // SYSTEM_POWER_STATUS.BatteryFlag
    private const byte Unknown = 255; // SYSTEM_POWER_STATUS.BatteryLifePercent

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    /// <summary>Null on a desktop with no battery, or if Windows can't report one.</summary>
    public static int? GetPercent()
    {
        if (!GetSystemPowerStatus(out var status)) return null;
        if (status.BatteryFlag == NoSystemBattery || status.BatteryLifePercent == Unknown) return null;
        return status.BatteryLifePercent;
    }
}
