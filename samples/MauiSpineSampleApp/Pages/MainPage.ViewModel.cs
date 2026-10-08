using System.Collections.ObjectModel;
using System.ComponentModel;

namespace MauiSpineSampleApp.Pages;

public partial class MainPageViewModel(INavigationService _navigation) : SampleViewModel
{

    // The theme button sits where the header bar's action sits on every other page, so it stays put
    // when a page is pushed. The collapsed hero is the bar's row below the status bar, with the title
    // and the theme button centred on one line.
    private static double CompactBar => HeaderBarConstants.Height;

    // On the Mac the theme button sits on the close button's line, whose centre is this far down from the
    // window's top edge (in the iPad idiom's points, which the Mac draws at 77 %). The
    // collapsed bar is centred on the window buttons, so the title shares their line.
    private const double MacCloseButtonCentre = 20;

    // The glass circle keeps this far from the window's right edge, clear of its rounded corner.
    private const double MacThemeButtonInset = 12;

    // The header bar's icon action: on iOS a glass circle as tall as the row, its edge at the page
    // margin; on Android Material 3's 40-point circle centred in the 48-point slot, so its icon lines up
    // with the page margin. The Mac keeps a smaller circle that matches the window buttons.
    private static double ThemeButton =>
        DeviceInfo.Platform == DevicePlatform.Android ? 40
        : DeviceInfo.Platform == DevicePlatform.MacCatalyst ? 32
        : HeaderBarConstants.Height;

    private static double ThemeButtonInset => DeviceInfo.Platform == DevicePlatform.Android
        ? HeaderBarConstants.RegionSideMargin + (HeaderBarConstants.RegionButtonWidth - ThemeButton) / 2
        : HeaderBarConstants.PageMargin;

    public double HeaderMinHeight => DeviceInfo.Platform == DevicePlatform.MacCatalyst
        ? 2 * MacCloseButtonCentre
        : SystemBarInsets.Top + CompactBar;

    // Tall enough that the photo's S starts below the status bar (and the Dynamic Island) rather than behind it.
    public double HeaderMaxHeight => SystemBarInsets.Top + 270;

    public Thickness GearMargin => DeviceInfo.Platform == DevicePlatform.WinUI
        ? new Thickness(0, 0, 144, 0)
        : DeviceInfo.Platform == DevicePlatform.MacCatalyst
            ? new Thickness(0, MacCloseButtonCentre - ThemeButton / 2, MacThemeButtonInset, 0)
            : new Thickness(0, SystemBarInsets.Top + (CompactBar - ThemeButton) / 2, ThemeButtonInset + SystemBarInsets.Right, 0);

    // The photo runs edge to edge; the title and the rows keep clear of the Dynamic Island and the
    // rounded corners in landscape.
    public Thickness TitleMargin => new(HeaderBarConstants.PageMargin + SystemBarInsets.Left, -4, HeaderBarConstants.PageMargin, -4);

    public Thickness ListMargin => new(SystemBarInsets.Left, 0, SystemBarInsets.Right, 0);

    public double FooterHeight => SystemBarInsets.Bottom;

    // Filled before the page appears, so the first frame already has the rows and their icons.
    public ObservableCollection<Item> Items { get; } = new(SampleIndex.OrderBy(i => i.Title, StringComparer.OrdinalIgnoreCase));

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName == nameof(SystemBarInsets))
        {
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(HeaderMinHeight)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(HeaderMaxHeight)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(GearMargin)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(TitleMargin)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(ListMargin)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(FooterHeight)));
        }
    }

    [RelayCommand]
    private async Task ItemTapped(Item item)
    {
        if (item.Open is { } open)
            await open(_navigation);
    }

    // One row per sample page. Add a page here when it gets a page of its own; the list shows them by title.
    private static IEnumerable<Item> SampleIndex =>
    [
        new("Bottom sheets", "Native sheets: heights, dim or blur, a guard against closing, a checkmark and an X in the header, a footer action", "sheet.svg", "Plugin.Maui.Spine", n => n.NavigateToAsync<Sheets.SheetsPage>()),
        new("Parameters and results", "Hand a page typed data, and await what it returns", "return.svg", "Plugin.Maui.Spine", n => n.NavigateToAsync<Results.ResultsPage>()),
        new("Page binding", "{PageCommand} and {PageBinding}: reach the page's view model from inside a row template", "link.svg", "Plugin.Maui.Spine", n => n.NavigateToAsync<PageBinding.PageBindingPage>()),
        new("Header bar", "Layout, large title, background, foreground and status bar: try any combination and see the code for it", "headerbar.svg", "Plugin.Maui.Spine", n => n.NavigateToAsync<HeaderBar.HeaderBarPage>()),
        new("Materials", "Blur, Liquid Glass or a tint behind any Border, from a preset or tuned by hand, and glass buttons that merge", "layers.svg", "Plugin.Maui.Spine", n => n.NavigateToAsync<Materials.MaterialsPage>()),
        new("Motion", "Depth that follows the phone's tilt: layers that move against each other, and a light that sweeps over a panel", "phone.svg", "Plugin.Maui.Spine", n => n.NavigateToAsync<Motion.MotionPage>()),
        new("Loading states", "TaskState and StateView: a spinner, the error with a retry, an empty line or a skeleton, from one load that lives with the page", "hourglass.svg", "Plugin.Maui.Spine", n => n.NavigateToAsync<State.StatePage>()),
        new("Page lifetime", "Poll, WhileVisible and PageLifetime: work that refreshes, listens and cancels with the page", "refresh.svg", "Plugin.Maui.Spine", n => n.NavigateToAsync<Lifetime.LifetimePage>()),
        new("Menu buttons", "Native menus from a header action, a pop-up button that shows its pick, and an icon button with sections, submenus and toggles", "more.svg", "Plugin.Maui.Spine, Plugin.Maui.Spine.Svg.Icons", n => n.NavigateToAsync<Menus.MenusPage>()),
        new("Context menus", "Long-press or right-click any view for the platform's own menu: a lifted card on iOS, one shared menu for every row of a list", "menu.svg", "Plugin.Maui.Spine, Plugin.Maui.Spine.Svg.Icons", n => n.NavigateToAsync<ContextMenus.ContextMenusPage>()),
        new("Segmented control", "The platform's own segmented control with text or icons, and top tabs that build each tab's content when it is first picked", "segmented.svg", "Plugin.Maui.Spine, Plugin.Maui.Spine.Svg.Icons", n => n.NavigateToAsync<Segmented.SegmentedPage>()),
        new("Action sheets", "Typed action sheets from a view model: rows with icons and a destructive one, the picked row as the result, a popover at the button on iPad", "list.svg", "Plugin.Maui.Spine, Plugin.Maui.Spine.Svg.Icons", n => n.NavigateToAsync<ActionSheets.ActionSheetsPage>()),
        new("Page actions", "A header button from a command; change its text, badge, enabled state and visibility live, or replace Back", "energy.svg", "Plugin.Maui.Spine, Plugin.Maui.Spine.Svg.Icons", n => n.NavigateToAsync<PageActions.PageActionsPage>()),
        new("Haptics", "Success, warning, error, selection and impacts from the platform's own generators, on a tap, a header action, a tab switch or a sheet", "haptic.svg", "Plugin.Maui.Spine", n => n.NavigateToAsync<Haptics.HapticsPage>()),
        new("Reorder", "Drag items of any CollectionView to a new place: a long-press or a grip handle, shown on demand with an Edit button, with haptics and screen-reader actions", "griphorizontal.svg", "Plugin.Maui.Spine", n => n.NavigateToAsync<Reorder.ReorderPage>()),
        new("Transitions", "A view carried from one page to the next and back, or a page that grows out of the view it opens from and shrinks back into it under the finger", "expand.svg", "Plugin.Maui.Spine", n => n.NavigateToAsync<Transitions.TransitionsPage>()),
        new("Lightbox", "Photos full screen on black: page through them, pinch or double-tap to zoom, and drag down to close into the thumbnail", "image.svg", "Plugin.Maui.Spine", n => n.NavigateToAsync<Photos.PhotosPage>()),
        new("Keyboard", "A field at the foot of the page stays above the on-screen keyboard and moves with it, in a region, a tab or a sheet", "keyboard.svg", "Plugin.Maui.Spine", n => n.NavigateToAsync<Keyboard.KeyboardPage>()),
        new("Liquid Glass", "Glass.Style turns a Button or ImageButton into Liquid Glass on iOS 26; the same markup is a normal button elsewhere", "water.svg", "Plugin.Maui.Spine, Plugin.Maui.Spine.Svg", n => n.NavigateToAsync<Glass.GlassPage>()),
        new("Scroll inset", "A list that runs behind the home indicator yet scrolls its last row clear, per page, per list or app-wide", "vertical.svg", "Plugin.Maui.Spine", n => n.NavigateToAsync<ScrollInset.ScrollInsetPage>()),
        new("Typography", "Digits that keep their width, and capitals centred in badges", "edit.svg", "Plugin.Maui.Spine", n => n.NavigateToAsync<Typography.TypographyPage>()),
        new("AnimatedLabel", "A line that scrolls when the text is too long, and fades when it changes", "text.svg", "Plugin.Maui.Spine.Controls.AnimatedLabel", n => n.NavigateToAsync<Marquee.MarqueePage>()),
        new("Barcodes", "QR, Data Matrix and linear codes from any text, a pairing code on a 12 × 12 word clock, and a camera scanner that reads both", "qrcode.svg", "Plugin.Maui.Spine.Barcodes, Plugin.Maui.Spine.Scanner", n => n.NavigateToAsync<Barcodes.BarcodesPage>()),
        new("Calendar", "Month calendar to pick a date, with days marked from your own service", "calendar.svg", "Plugin.Maui.Spine.Controls.Calendar", n => n.NavigateToAsync<Dates.DatesPage>()),
        new("DataGrid", "One set of columns, a layout per width: a table when wide, two-line rows on a phone", "grid.svg", "Plugin.Maui.Spine.Controls.DataGrid, Plugin.Maui.Spine.Svg.Icons", n => n.NavigateToAsync<DataGrid.DataGridPage>()),
        new("HeroCollectionView", "A list under a photo that collapses to a compact header: the photo centred or sliding, a blur that fades in, a stretch when pulled", "image.svg", "Plugin.Maui.Spine.Controls.HeroCollectionView", n => n.NavigateToAsync<Hero.HeroPage>()),
        new("MeshBackground", "A mesh gradient behind glass: coloured points blended smoothly, drifting slowly, from the accent, a preset or your own colours", "sunset.svg", "Plugin.Maui.Spine.Controls.MeshBackground", n => n.NavigateToAsync<Mesh.MeshPage>()),
        new("Rows", "Settings and key/value rows in one control, any view as a button with press feedback, one screen-reader element per row", "list.svg", "Plugin.Maui.Spine.Controls.Rows, Plugin.Maui.Spine", n => n.NavigateToAsync<Rows.RowsPage>()),
        new("Shimmer", "Loading placeholders: a shimmer over empty blocks, or the real layout as its own skeleton", "lightstrip.svg", "Plugin.Maui.Spine.Controls.Shimmer", n => n.NavigateToAsync<Shimmer.ShimmerPage>()),
        new("Theming", "Light, dark or the system, an app-wide accent, colours that follow the theme, a repaint hook for code-drawn views", "theme.svg", "Plugin.Maui.Spine", n => n.NavigateToAsync<Theme.ThemePage>()),
        new("Widgets", "A live score on the home screen and the Lock Screen, written in C#: buttons that run without opening the app, a timeline that turns at face-off", "stack.svg", "Plugin.Maui.Spine.Widgets", n => n.NavigateToAsync<Widgets.WidgetsPage>()),
        new("Live Activities", "A game on the lock screen and in the Dynamic Island, updated from the app: a countdown to face-off, the score, a button at the final whistle", "timer.svg", "Plugin.Maui.Spine.Widgets", n => n.NavigateToAsync<LiveActivities.LiveActivitiesPage>()),
        new("Shortcuts", "Straight to a page from the app icon, the jump list or the tray menu, through one handler", "next.svg", "Plugin.Maui.Spine", n => n.NavigateToAsync<Shortcuts.ShortcutsPage>()),
        new("Strings", "Text per language from embedded XML: values and plurals in XAML, the same store from C#, a live language switch", "globe.svg", "Plugin.Maui.Spine, Plugin.Maui.Spine.Common", n => n.NavigateToAsync<Strings.StringsPage>()),
        new("SVG icons", "Sharp, theme-tinted icons from SVG: tint only the outline, dark tones for coloured art, line weight per size, 224 bundled icons", "fish.svg", "Plugin.Maui.Spine.Svg, Plugin.Maui.Spine.Svg.Icons", n => n.NavigateToAsync<SvgIcons.SvgIconsPage>()),
    ];
}

public partial class Item : ObservableObject
{
    public Item() { }

    public Item(string title, string description, string icon, string packages, Func<INavigationService, Task> open)
    {
        Title = title;
        Description = description;
        Icon = icon;
        Packages = packages;
        Open = open;
    }

    public Func<INavigationService, Task>? Open { get; init; }

    /// <summary>Comma-separated NuGet ids the sample depends on.</summary>
    public string? Packages { get; init; }

    [ObservableProperty]
    public partial string? Icon { get; set; }

    [ObservableProperty]
    public partial string? Title { get; set; }

    [ObservableProperty]
    public partial string? Description { get; set; }

    [ObservableProperty]
    public partial bool IsMovable { get; set; }
}