namespace CartridgeOS.Launcher.ViewModels;

/// <summary>One entry in the screen saver's inactivity-duration combo box (Settings).</summary>
public sealed record InactivityOption(int Minutes, string Label);
