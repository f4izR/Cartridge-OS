using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace CartridgeOS.Launcher.Views;

public partial class SettingsPanel : UserControl
{
    public SettingsPanel()
    {
        InitializeComponent();
    }

    private void PreviewScreenSaver_Click(object sender, RoutedEventArgs e) =>
        ((App)Application.Current).ShowScreenSaverNow();

    /// <summary>Fades the tab content in on every category switch. Filtered to the TabControl itself
    /// (not code-behind-level EventTrigger, see SettingsTabControlStyle's comment) — Selector.SelectionChanged
    /// bubbles, and several ComboBoxes inside the tab content are Selectors too, so their own selection
    /// changes would otherwise also fire this.</summary>
    private void CategoryTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, CategoryTabControl)) return;
        if (CategoryTabControl.Template.FindName("ContentHost", CategoryTabControl) is not FrameworkElement contentHost) return;

        contentHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.25)));
    }
}
