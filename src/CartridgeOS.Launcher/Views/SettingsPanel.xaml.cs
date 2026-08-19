using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using System.Windows.Threading;

namespace CartridgeOS.Launcher.Views;

public partial class SettingsPanel : UserControl
{
    public SettingsPanel()
    {
        InitializeComponent();
    }

    private void PreviewScreenSaver_Click(object sender, RoutedEventArgs e) =>
        ((App)Application.Current).ShowScreenSaverNow();

    /// <summary>Shared by every in-app Hyperlink (currently the two artwork-provider signup links) — WPF
    /// Hyperlinks don't open anything themselves, they just raise this. UseShellExecute is required for
    /// a URI ProcessStartInfo target since .NET Core dropped the old implicit shell-execute default.</summary>
    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

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

    private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {

    }

    /// <summary>Nothing here ever had keyboard focus before this panel opened (it's a sibling of the
    /// library grid, not shown over it), so D-Pad's MoveFocus calls in MainWindow.HandleGamepadAction had
    /// no starting point and silently did nothing. Seeding focus onto the tab strip itself on open fixes
    /// that — same fix PowerMenuWindow already had via its own explicit Focus() call on Loaded.
    /// Deferred a tick (Dispatcher, not called inline) because IsVisibleChanged fires before the
    /// now-visible subtree has been measured/arranged — calling MoveFocus synchronously here sometimes
    /// found no focusable descendant yet and silently did nothing.</summary>
    private void RootBorder_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Debug.WriteLine($"[SettingsPanel] IsVisibleChanged -> {e.NewValue}");
        if ((bool)e.NewValue) Dispatcher.BeginInvoke(FocusFirst, DispatcherPriority.Loaded);
    }

    /// <summary>Moves keyboard focus onto the tab strip so D-Pad nav has somewhere to start from —
    /// called on open (above) and again as a fallback from MainWindow if a nav press arrives while focus
    /// somehow isn't inside this panel at all (e.g. it got stolen by something else in the meantime).</summary>
    public void FocusFirst()
    {
        bool moved = CategoryTabControl.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        Debug.WriteLine($"[SettingsPanel] FocusFirst -> moved={moved}, focused={Keyboard.FocusedElement}");
    }
}
