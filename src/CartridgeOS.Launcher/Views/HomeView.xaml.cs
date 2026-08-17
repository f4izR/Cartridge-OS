using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CartridgeOS.Launcher.ViewModels;

namespace CartridgeOS.Launcher.Views;

public partial class HomeView : UserControl
{
    // Tile sizes and horizontal spacing for the carousel — kept here rather than in the DataTemplate
    // since position and size are computed together (ApplyOffset) and driven by code, not XAML triggers.
    // Heights sized so the image row (tile height minus the title row beneath it) lands close to a 2:3
    // portrait box-art ratio at each tile's width — the old 360/264 heights made the image row noticeably
    // wider than 2:3, so UniformToFill (see HomeView.xaml's ArtworkImage) had to crop a lot more off the
    // top/bottom than real box art needs, cropping in on characters/logos. Widths unchanged.
    private const double CenterWidth = 260, CenterHeight = 430;
    private const double SideWidth = 190, SideHeight = 320;
    private const double SlotPitch = 250; // horizontal distance between adjacent offsets' centers
    private const double CanvasCenterX = (2 * MainViewModel.HomeCarouselSideCount + 1) * SlotPitch / 2; // matches the Canvas width in HomeView.xaml
    private static readonly TimeSpan SlideDuration = TimeSpan.FromMilliseconds(340);

    public HomeView()
    {
        InitializeComponent();
    }

    /// <summary>Single click selects the tile (recomputing HomeCarouselSlots around it); a double-click
    /// also launches — same combined pattern the old ListBox's SelectedItem+MouseDoubleClick gave for free,
    /// reimplemented by hand now that the carousel isn't a Selector control anymore.</summary>
    private void HomeCarouselTile_Click(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not HomeCarouselSlot slot) return;

        var vm = (MainViewModel)DataContext;
        vm.SelectedGame = slot.Game;
        if (e.ClickCount == 2) MainWindow.LaunchSelected(vm, slot.Game);
    }

    private void HomePlay_Click(object sender, RoutedEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        MainWindow.LaunchSelected(vm, vm.SelectedGame);
    }

    /// <summary>
    /// Carousel tiles are reused for as long as their game stays in the visible window (see
    /// MainViewModel.RefreshHomeCarouselSlots), so a Left/Right press updates an existing slot's Offset
    /// rather than recreating the tile — this places it at its Offset's position/size immediately on
    /// first appearance, then animates both whenever Offset subsequently changes, which is what makes the
    /// carousel actually slide and grow/shrink instead of just swapping content in a fixed spot.
    /// </summary>
    private void HomeCarouselTile_Loaded(object sender, RoutedEventArgs e)
    {
        var tile = (FrameworkElement)sender;
        if (tile.DataContext is not HomeCarouselSlot slot) return;
        // Canvas.Left/Top only positions a Canvas's direct children — that's the ContentPresenter WPF
        // generates for each item, not this DataTemplate's own root ("tile"), which sits one level deeper.
        if (VisualTreeHelper.GetParent(tile) is not FrameworkElement container) return;

        ApplyOffset(tile, container, slot.Offset, animate: false);

        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName == nameof(HomeCarouselSlot.Offset)) ApplyOffset(tile, container, slot.Offset, animate: true);
        };
        slot.PropertyChanged += handler;
        tile.Unloaded += (_, _) => slot.PropertyChanged -= handler;
    }

    private static void ApplyOffset(FrameworkElement tile, FrameworkElement container, int offset, bool animate)
    {
        bool isCenter = offset == 0;
        double width = isCenter ? CenterWidth : SideWidth;
        double height = isCenter ? CenterHeight : SideHeight;
        double left = CanvasCenterX + offset * SlotPitch - width / 2;
        double top = CenterHeight - height; // bottom-aligned: tiles grow upward from a shared baseline

        if (!animate)
        {
            tile.Width = width;
            tile.Height = height;
            Canvas.SetLeft(container, left);
            Canvas.SetTop(container, top);
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Animate(tile, FrameworkElement.WidthProperty, width, ease);
        Animate(tile, FrameworkElement.HeightProperty, height, ease);
        Animate(container, Canvas.LeftProperty, left, ease);
        Animate(container, Canvas.TopProperty, top, ease);
    }

    private static void Animate(FrameworkElement tile, DependencyProperty property, double to, IEasingFunction ease) =>
        tile.BeginAnimation(property, new DoubleAnimation(to, SlideDuration) { EasingFunction = ease });
}
