using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.Maui.Controls.Shapes;
using Plugin.Maui.Spine.Extensions;

namespace MauiSpineSampleApp.Pages;

public partial class MainPageViewModel : SampleViewModel
{
    private readonly INavigationService _navigation;
    private readonly SpineOptions _options;
    private readonly IReadOnlyList<Item> _all;
    private readonly MenuPicker _showPicker;
    private readonly MenuPicker _packagePicker;

    public MainPageViewModel(INavigationService navigation, SpineOptions options)
    {
        _navigation = navigation;
        _options = options;
        _all = [.. SampleIndex.OrderBy(i => i.Title, StringComparer.OrdinalIgnoreCase)];
        Items = new(_all);

        _showPicker = new MenuPicker(ShowCategoryCommand) { new MenuAction("All samples", "list.svg") { IsChecked = true } };
        foreach (var category in SampleCategory.All)
            _showPicker.Add(new MenuAction(category.Name, category.Svg) { CommandParameter = category });

        _packagePicker = new MenuPicker(ShowPackageCommand);
        foreach (var package in _all.SelectMany(i => i.PackageIds).Distinct().OrderBy(ShortPackage, StringComparer.OrdinalIgnoreCase))
            _packagePicker.Add(new MenuAction(ShortPackage(package)) { CommandParameter = package });

        FilterMenu =
        [
            new MenuSection("Show") { _showPicker },
            new SubMenu("Package", "stack.svg") { _packagePicker },
        ];
    }

    // The buttons sit where the header bar's actions sit on every other page. The collapsed hero is the
    // bar's row below the status bar, with the title and the buttons centred on one line.
    private static double CompactBar => HeaderBarConstants.Height;

    // On the Mac the buttons sit on the close button's line, whose centre is this far down from the
    // window's top edge (in the iPad idiom's points, which the Mac draws at 77 %). The
    // collapsed bar is centred on the window buttons, so the title shares their line.
    private const double MacCloseButtonCentre = 20;

    // The glass circle keeps this far from the window's right edge, clear of its rounded corner.
    private const double MacButtonInset = 12;

    // The header bar's icon action: on iOS a glass circle as tall as the row, its edge at the page
    // margin; on Android Material 3's 40-point circle centred in the 48-point slot, so its icon lines up
    // with the page margin. The Mac keeps a smaller circle that matches the window buttons.
    private static double ButtonSize =>
        DeviceInfo.Platform == DevicePlatform.Android ? 40
        : DeviceInfo.Platform == DevicePlatform.MacCatalyst ? 32
        : DeviceInfo.Platform == DevicePlatform.WinUI ? 48
        : HeaderBarConstants.Height;

    private static double ButtonInset => DeviceInfo.Platform == DevicePlatform.Android
        ? HeaderBarConstants.RegionSideMargin + (HeaderBarConstants.RegionButtonWidth - ButtonSize) / 2
        : HeaderBarConstants.PageMargin;

    // As tall as the status bar and header bar on the other pages (HeroPage's SafeAreaInsets.Top), so the
    // compact hero is the same height everywhere; on iOS 26 the bar is taller than its button row.
    public double HeaderMinHeight => DeviceInfo.Platform == DevicePlatform.MacCatalyst
        ? 2 * MacCloseButtonCentre
        : SystemBarInsets.Top + HeaderBarConstants.BarHeight;

    // Tall enough that the photo's S starts below the status bar (and the Dynamic Island) rather than behind it.
    public double HeaderMaxHeight => SystemBarInsets.Top + 270;

    // Settings and filter share one glass capsule where the header bar groups its actions
    // (SpineOptions.Apple.GroupHeaderActions), as the other pages' header bars do; elsewhere each is a circle of its own.
    public bool GroupsButtons => (OperatingSystem.IsIOSVersionAtLeast(26) || OperatingSystem.IsMacCatalystVersionAtLeast(26))
        && _options.Apple.GlassHeaderActions && _options.Apple.GroupHeaderActions;

    public GlassStyle ButtonGlass => GroupsButtons ? GlassStyle.Transient : GlassStyle.Clear;

    public MaterialPreset ButtonGroupMaterial => GroupsButtons ? MaterialPreset.GlassClear : MaterialPreset.None;

    private double ButtonSpacing => GroupsButtons ? 0 : HeaderBarConstants.TitleActionGap;

    public RoundRectangle ButtonGroupShape => new() { CornerRadius = ButtonSize / 2 };

    public Thickness GearMargin => DeviceInfo.Platform == DevicePlatform.WinUI
        ? new Thickness(0, 0, 144, 0)
        : DeviceInfo.Platform == DevicePlatform.MacCatalyst
            ? new Thickness(0, MacCloseButtonCentre - ButtonSize / 2, MacButtonInset, 0)
            : new Thickness(0, SystemBarInsets.Top + (CompactBar - ButtonSize) / 2, ButtonInset + SystemBarInsets.Right, 0);

    // Where the filter button sits once the header has collapsed: beside settings, on its line.
    public Thickness FilterMargin => GearMargin with { Right = GearMargin.Right + ButtonSize + ButtonSpacing };

    // How far right of FilterMargin the filter button sits while the header is open: at the page's edge,
    // below settings (Windows keeps its caption buttons' corner free only at the top).
    public double FilterSlide => FilterMargin.Right - (DeviceInfo.Platform == DevicePlatform.WinUI
        ? HeaderBarConstants.PageMargin
        : GearMargin.Right);

    // The photo runs edge to edge; the title and the rows keep clear of the Dynamic Island and the
    // rounded corners in landscape.
    public Thickness TitleMargin => new(HeaderBarConstants.PageMargin + SystemBarInsets.Left, -4, HeaderBarConstants.PageMargin, -4);

    public Thickness ListMargin => new(SystemBarInsets.Left, 0, SystemBarInsets.Right, 0);

    public double FooterHeight => SystemBarInsets.Bottom;

    // Filled before the page appears, so the first frame already has the rows and their icons.
    public ObservableCollection<Item> Items { get; }

    /// <summary>Raised after a new filter has changed the rows, so the page can scroll back to the top.</summary>
    public event EventHandler? FilterChanged;

    /// <summary>The filter button's menu: all samples, one area, or the samples of one package.</summary>
    public MenuItems FilterMenu { get; }

    public string FilterDescription => _showPicker.Selected is { CommandParameter: SampleCategory category }
        ? $"Filter samples, showing {category.Name}"
        : _packagePicker.Selected is { } package
            ? $"Filter samples, showing {package.Title}"
            : "Filter samples";

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName == nameof(SystemBarInsets))
        {
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(HeaderMinHeight)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(HeaderMaxHeight)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(GearMargin)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(FilterMargin)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(FilterSlide)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(TitleMargin)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(ListMargin)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(FooterHeight)));
        }
    }

    // One filter at a time: a pick in one picker clears the other.
    // "All samples" has no parameter, so the picker hands over the action itself.
    [RelayCommand]
    private void ShowCategory(object? choice)
    {
        _packagePicker.Selected = null;
        ApplyFilter(choice is SampleCategory category ? i => i.Category == category : _ => true);
    }

    [RelayCommand]
    private void ShowPackage(string package)
    {
        _showPicker.Selected = null;
        ApplyFilter(i => i.PackageIds.Contains(package));
    }

    // Moves the rows rather than replacing the list, so the ones that stay keep their place on screen and
    // the others fade in and out.
    private void ApplyFilter(Func<Item, bool> show)
    {
        var shown = _all.Where(show).ToList();

        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (!shown.Contains(Items[i]))
                Items.RemoveAt(i);
        }

        for (var i = 0; i < shown.Count; i++)
        {
            if (i < Items.Count && ReferenceEquals(Items[i], shown[i]))
                continue;

            var from = Items.IndexOf(shown[i]);
            if (from >= 0)
                Items.Move(from, i);
            else
                Items.Insert(i, shown[i]);
        }

        OnPropertyChanged(nameof(FilterDescription));
        FilterChanged?.Invoke(this, EventArgs.Empty);
    }

    // As the rows' pills show it: the common prefix is implied.
    private static string ShortPackage(string id) =>
        id.StartsWith("Plugin.Maui.Spine", StringComparison.Ordinal) ? "Spine" + id["Plugin.Maui.Spine".Length..] : id;

    [RelayCommand]
    private Task OpenSettings() => _navigation.NavigateToAsync<Settings.SettingsPage>();

    [RelayCommand]
    private async Task ItemTapped(Item item)
    {
        if (item.Open is { } open)
            await open(_navigation);
    }

    // One row per sample page. Add a page here when it gets a page of its own; the list shows them by title.
    private static IEnumerable<Item> SampleIndex =>
    [
        new("Bottom sheets", "Native sheets: heights, dim or blur, a guard against closing, a checkmark and an X in the header, a footer action", "sheet.svg", "Plugin.Maui.Spine", SampleCategory.Pages, n => n.NavigateToAsync<Sheets.SheetsPage>()),
        new("Markdown", "Markdown drawn with native views: headings, lists, code with Copy, quotes, tables and images, themed by style options; a streamed answer rebuilds only its last block", "text.svg", "Plugin.Maui.Spine.Controls.Markdown", SampleCategory.Controls, n => n.NavigateToAsync<Markdown.MarkdownPage>()),
        new("Parameters and results", "Hand a page typed data, and await what it returns", "return.svg", "Plugin.Maui.Spine", SampleCategory.Pages, n => n.NavigateToAsync<Results.ResultsPage>()),
        new("Page binding", "{PageCommand} and {PageBinding}: reach the page's view model from inside a row template", "link.svg", "Plugin.Maui.Spine", SampleCategory.Pages, n => n.NavigateToAsync<PageBinding.PageBindingPage>()),
        new("Header bar", "Layout, large title, background, foreground and status bar: try any combination and see the code for it", "headerbar.svg", "Plugin.Maui.Spine", SampleCategory.Pages, n => n.NavigateToAsync<HeaderBar.HeaderBarPage>()),
        new("Materials", "Blur, Liquid Glass or a tint behind any Border, from a preset or tuned by hand, and glass buttons that merge", "layers.svg", "Plugin.Maui.Spine", SampleCategory.Look, n => n.NavigateToAsync<Materials.MaterialsPage>()),
        new("Motion", "Depth that follows the phone's tilt: layers that move against each other, and a light that sweeps over a panel", "axes3d.svg", "Plugin.Maui.Spine", SampleCategory.Look, n => n.NavigateToAsync<Motion.MotionPage>()),
        new("Loading states", "TaskState and StateView: a spinner, the error with a retry, an empty line or a skeleton, from one load that lives with the page", "hourglass.svg", "Plugin.Maui.Spine", SampleCategory.Pages, n => n.NavigateToAsync<State.StatePage>()),
        new("Page lifetime", "Poll, WhileVisible and PageLifetime: work that refreshes, listens and cancels with the page", "refresh.svg", "Plugin.Maui.Spine", SampleCategory.Pages, n => n.NavigateToAsync<Lifetime.LifetimePage>()),
        new("Menu buttons", "Native menus from a header action, a pop-up button that shows its pick, and an icon button with sections, submenus and toggles", "more.svg", "Plugin.Maui.Spine, Plugin.Maui.Spine.Svg.Icons", SampleCategory.Menus, n => n.NavigateToAsync<Menus.MenusPage>()),
        new("Context menus", "Long-press or right-click any view for the platform's own menu: a lifted card on iOS, one shared menu for every row of a list", "menu.svg", "Plugin.Maui.Spine, Plugin.Maui.Spine.Svg.Icons", SampleCategory.Menus, n => n.NavigateToAsync<ContextMenus.ContextMenusPage>()),
        new("Search", "A search field with the header bar from [PageSearch] on a string property: a row below the bar, the trailing end of the bar on iPad and Mac, in a region or a sheet", "search.svg", "Plugin.Maui.Spine", SampleCategory.Pages, n => n.NavigateToAsync<Search.SearchPage>()),
        new("Segmented control", "The platform's own segmented control with text or icons, and top tabs that build each tab's content when it is first picked", "segmented.svg", "Plugin.Maui.Spine, Plugin.Maui.Spine.Svg.Icons", SampleCategory.Menus, n => n.NavigateToAsync<Segmented.SegmentedPage>()),
        new("Action sheets", "Typed action sheets from a view model: rows with icons and a destructive one, the picked row as the result, a popover at the button on iPad", "list.svg", "Plugin.Maui.Spine, Plugin.Maui.Spine.Svg.Icons", SampleCategory.Menus, n => n.NavigateToAsync<ActionSheets.ActionSheetsPage>()),
        new("Sign-in", "Email and password or Sign in with Apple against the app's own server, a page and a command behind [RequiresSignIn], calls with the access token", "user.svg", "Plugin.Maui.Spine.Authentication", SampleCategory.Security, n => n.NavigateToAsync<SignIn.SignInPage>()),
        new("Biometrics", "Face ID, Touch ID, fingerprint or Windows Hello before a page opens or a command runs, an unlock that counts for a minute", "lock.svg", "Plugin.Maui.Spine.Biometrics", SampleCategory.Security, n => n.NavigateToAsync<Biometrics.BiometricsPage>()),
        new("Guards", "Ask before a page opens or a command runs: one attribute on the page or the command, answered by a guard you register", "lock.svg", "Plugin.Maui.Spine", SampleCategory.Security, n => n.NavigateToAsync<Guards.GuardsPage>()),
        new("Page actions", "A header button from a command; change its text, badge, enabled state and visibility live, or replace Back", "energy.svg", "Plugin.Maui.Spine, Plugin.Maui.Spine.Svg.Icons", SampleCategory.Pages, n => n.NavigateToAsync<PageActions.PageActionsPage>()),
        new("Haptics", "Success, warning, error, selection and impacts from the platform's own generators, on a tap, a header action, a tab switch or a sheet", "haptic.svg", "Plugin.Maui.Spine", SampleCategory.Menus, n => n.NavigateToAsync<Haptics.HapticsPage>()),
        new("Reorder", "Drag items of any CollectionView to a new place: a long-press or a grip handle, shown on demand with an Edit button, with haptics and screen-reader actions", "sort.svg", "Plugin.Maui.Spine", SampleCategory.Menus, n => n.NavigateToAsync<Reorder.ReorderPage>()),
        new("Transitions", "A view carried from one page to the next and back, or a page that grows out of the view it opens from and shrinks back into it under the finger", "expand.svg", "Plugin.Maui.Spine", SampleCategory.Pages, n => n.NavigateToAsync<Transitions.TransitionsPage>()),
        new("Lightbox", "Photos full screen on black: page through them, pinch or double-tap to zoom, and drag down to close into the thumbnail", "image.svg", "Plugin.Maui.Spine", SampleCategory.Media, n => n.NavigateToAsync<Photos.PhotosPage>()),
        new("Remote images", "Two hundred large photos in a grid that stays light: decoded at the cell's size off the main thread, cached in memory and on disk, prefetched, with BlurHash placeholders", "cloud.svg", "Plugin.Maui.Spine.Images", SampleCategory.Media, n => n.NavigateToAsync<Images.ImagesPage>()),
        new("Keyboard", "A field at the foot of the page stays above the on-screen keyboard and moves with it, in a region, a tab or a sheet", "keyboard.svg", "Plugin.Maui.Spine", SampleCategory.Pages, n => n.NavigateToAsync<Keyboard.KeyboardPage>()),
        new("Liquid Glass", "Glass.Style turns a Button or ImageButton into Liquid Glass on iOS 26; the same markup is a normal button elsewhere", "water.svg", "Plugin.Maui.Spine, Plugin.Maui.Spine.Svg", SampleCategory.Look, n => n.NavigateToAsync<Glass.GlassPage>()),
        new("Scroll inset", "A list that runs behind the home indicator yet scrolls its last row clear, per page, per list or app-wide", "vertical.svg", "Plugin.Maui.Spine", SampleCategory.Pages, n => n.NavigateToAsync<ScrollInset.ScrollInsetPage>()),
        new("Typography", "Digits that keep their width, and capitals centred in badges", "edit.svg", "Plugin.Maui.Spine", SampleCategory.Look, n => n.NavigateToAsync<Typography.TypographyPage>()),
        new("AnimatedLabel", "A line that scrolls when the text is too long, and fades when it changes", "text.svg", "Plugin.Maui.Spine.Controls.AnimatedLabel", SampleCategory.Controls, n => n.NavigateToAsync<Marquee.MarqueePage>()),
        new("Barcodes", "QR, Data Matrix and linear codes from any text, a pairing code on a 12 × 12 word clock, and a camera scanner that reads both", "qrcode.svg", "Plugin.Maui.Spine.Barcodes, Plugin.Maui.Spine.Scanner", SampleCategory.Media, n => n.NavigateToAsync<Barcodes.BarcodesPage>()),
        new("Chat", "One conversation, typed or spoken: answers streamed in as markdown, a composer with Stop, suggestions and a microphone, and a call with a realtime model that knows what was typed, with an avatar that follows it; functions on the phone and on the server", "chat.svg", "Plugin.Maui.Spine.Controls.Chat, Plugin.Maui.Spine.Voice, Plugin.Maui.Spine.Controls.Avatar", SampleCategory.AI, n => n.NavigateToAsync<Chat.ChatPage>()),
        new("Calendar", "Month calendar to pick a date, with days marked from your own service", "calendar.svg", "Plugin.Maui.Spine.Controls.Calendar", SampleCategory.Controls, n => n.NavigateToAsync<Dates.DatesPage>()),
        new("Masonry layout", "Cards in as many columns as fit, each in the shortest column so cards of different heights leave no holes; drag one to a new place, and pick a window that fits one, two or three columns on the desktop", "grid.svg", "Plugin.Maui.Spine", SampleCategory.Controls, n => n.NavigateToAsync<Masonry.MasonryPage>()),
        new("DataGrid", "One set of columns, a layout per width: a table when wide, two-line rows on a phone", "grid.svg", "Plugin.Maui.Spine.Controls.DataGrid, Plugin.Maui.Spine.Svg.Icons", SampleCategory.Controls, n => n.NavigateToAsync<DataGrid.DataGridPage>()),
        new("AI client", "One IChatClient per named feature: Apple Intelligence on the device first, then the app's server, streamed as it is written; off, budget and rate limits in one shape", "chat.svg", "Plugin.Maui.Spine.AI", SampleCategory.AI, n => n.NavigateToAsync<AIClient.AIClientPage>()),
        new("Voice lab", "Microphone and speaker at once with the platform's echo cancellation: levels and 24 bands each way, a reference to talk over, and an echo test that measures what reaches the microphone", "chat.svg", "Plugin.Maui.Spine.Voice", SampleCategory.AI, n => n.NavigateToAsync<VoiceLab.VoiceLabPage>()),
        new("Avatars", "Characters and ambient visuals from .spineavatar files: a robot and a plush in 3D, a vector totem and a flowing shader, one downloaded on request; drive one by hand or let it mouth text", "user.svg", "Plugin.Maui.Spine.Controls.Avatar", SampleCategory.AI, n => n.NavigateToAsync<Avatars.AvatarsPage>()),
        new("Avatar guide", "An avatar that presents: a caption that fills in as it speaks, a short script with a nod and a glance, a tour with the guide in its card looking at each control, and a one-step hint", "star.svg", "Plugin.Maui.Spine.Controls.Avatar, Plugin.Maui.Spine.Controls.Highlight", SampleCategory.AI, n => n.NavigateToAsync<AvatarGuide.AvatarGuidePage>()),
        new("Assistant", "A voice assistant with a face: an avatar that listens with your level, thinks, speaks with the answer and flinches when you talk over it, beside the transcript, with typing, mute and push-to-talk", "chat.svg", "Plugin.Maui.Spine.Controls.Avatar", SampleCategory.AI, n => n.NavigateToAsync<Assistant.AssistantPage>()),
        new("Generated images", "Images a model makes once on the server: a spinner while it is made, the picture when it is ready, the reason when it fails; asked again after Retry-After and not in the background", "image.svg", "Plugin.Maui.Spine.AI", SampleCategory.AI, n => n.NavigateToAsync<GeneratedImages.GeneratedImagesPage>()),
        new("Highlight", "Point at a view: a moving rim while a model works, rings that say tap here, a sweep, a running light or a breathing halo, once per install with a key", "star.svg", "Plugin.Maui.Spine.Controls.Highlight", SampleCategory.Controls, n => n.NavigateToAsync<Highlight.HighlightPage>()),
        new("HeroCollectionView", "A list under a photo that collapses to a compact header: the photo centred or sliding, a blur that fades in, a stretch when pulled", "image.svg", "Plugin.Maui.Spine.Controls.HeroCollectionView", SampleCategory.Controls, n => n.NavigateToAsync<Hero.HeroPage>()),
        new("MeshBackground", "A mesh gradient behind glass: coloured points blended smoothly, drifting slowly, from the accent, a preset or your own colours", "meshbackground.svg", "Plugin.Maui.Spine.Controls.MeshBackground", SampleCategory.Look, n => n.NavigateToAsync<Mesh.MeshPage>()),
        new("Rows", "Settings and key/value rows in one control, text fields as rows, any view as a button with press feedback, one screen-reader element per row", "list.svg", "Plugin.Maui.Spine.Controls.Rows, Plugin.Maui.Spine", SampleCategory.Controls, n => n.NavigateToAsync<Rows.RowsPage>()),
        new("Shimmer", "Loading placeholders: a shimmer over empty blocks, or the real layout as its own skeleton", "skeleton.svg", "Plugin.Maui.Spine.Controls.Shimmer", SampleCategory.Controls, n => n.NavigateToAsync<Shimmer.ShimmerPage>()),
        new("Theming", "Light, dark or the system, an app-wide accent, colours that follow the theme, a repaint hook for code-drawn views", "theme.svg", "Plugin.Maui.Spine", SampleCategory.Look, n => n.NavigateToAsync<Theme.ThemePage>()),
        new("Push notifications", "Push from the Showcase server to this device: permission, token and tags, a send from the app itself, buttons and replies, one handler for every message, and a widget a push rebuilds", "bell.svg", "Plugin.Maui.Spine.PushNotifications, Plugin.Maui.Spine.Server", SampleCategory.Outside, n => n.NavigateToAsync<Push.PushPage>()),
        new("Local notifications", "Notifications the device shows by itself: a plan that replaces the last one, with buttons, a picture and a sound, opened through the same handler as a push", "notification.svg", "Plugin.Maui.Spine.PushNotifications", SampleCategory.Outside, n => n.NavigateToAsync<LocalNotifications.LocalNotificationsPage>()),
        new("Background tasks", "Work the system runs while the app is closed: a [BackgroundTask] class, its status, a run on request, and the widgets rebuilt after it", "clock.svg", "Plugin.Maui.Spine.BackgroundTasks", SampleCategory.Outside, n => n.NavigateToAsync<BackgroundTasks.BackgroundTasksPage>()),
        new("Widgets", "A live score on the home screen and the Lock Screen, written in C#: buttons that run without opening the app, a timeline that turns at face-off", "stack.svg", "Plugin.Maui.Spine.Widgets", SampleCategory.Outside, n => n.NavigateToAsync<Widgets.WidgetsPage>()),
        new("Layout preview", "A widget or Live Activity drawn in the app at every place and size it is shown, iOS or Android, light or dark, with what Explain() says, and a layout file read as one", "stack.svg", "Plugin.Maui.Spine.Widgets", SampleCategory.Outside, n => n.NavigateToAsync<Widgets.LayoutPreviewPage>()),
        new("Live Activities", "A game on the lock screen and in the Dynamic Island, updated from the app: a countdown to face-off, the score, a button at the final whistle", "timer.svg", "Plugin.Maui.Spine.Widgets", SampleCategory.Outside, n => n.NavigateToAsync<LiveActivities.LiveActivitiesPage>()),
        new("Control Center", "A toggle and a button in Control Center and Quick Settings, written in C#: goal alerts on or off, and a goal scored without opening the app", "remote.svg", "Plugin.Maui.Spine.Widgets", SampleCategory.Outside, n => n.NavigateToAsync<ControlCenter.ControlCenterPage>()),
        new("Searchable items", "The app's content in Spotlight and as Android shortcuts, each result opening its page with a typed parameter, also from a cold start", "searchitem.svg", "Plugin.Maui.Spine", SampleCategory.Outside, n => n.NavigateToAsync<SearchableItems.SearchableItemsPage>()),
        new("Shortcuts", "Straight to a page from the app icon, the jump list or the tray menu, through one handler", "externallink.svg", "Plugin.Maui.Spine", SampleCategory.Outside, n => n.NavigateToAsync<Shortcuts.ShortcutsPage>()),
        new("Strings", "Text per language from embedded XML: values and plurals in XAML, the same store from C#, a live language switch", "globe.svg", "Plugin.Maui.Spine, Plugin.Maui.Spine.Common", SampleCategory.Look, n => n.NavigateToAsync<Strings.StringsPage>()),
        new("SVG icons", "Sharp, theme-tinted icons from SVG: tint only the outline, dark tones for coloured art, line weight per size, 242 bundled icons", "fish.svg", "Plugin.Maui.Spine.Svg, Plugin.Maui.Spine.Svg.Icons", SampleCategory.Look, n => n.NavigateToAsync<SvgIcons.SvgIconsPage>()),
    ];
}

public partial class Item : ObservableObject
{
    public Item() { }

    public Item(string title, string description, string icon, string packages, SampleCategory category, Func<INavigationService, Task> open)
    {
        Title = title;
        Description = description;
        Icon = icon;
        Packages = packages;
        Category = category;
        Open = open;
    }

    /// <summary>The area of Spine the sample belongs to, for the start page's filter.</summary>
    public SampleCategory? Category { get; init; }

    public IReadOnlyList<string> PackageIds => field ??= (Packages ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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
/// <summary>An area of Spine on the start page's filter menu; every sample belongs to one.</summary>
public sealed record SampleCategory(string Name, string Svg)
{
    public static SampleCategory Pages { get; } = new("Pages and navigation", "sheet.svg");
    public static SampleCategory Menus { get; } = new("Menus and gestures", "menu.svg");
    public static SampleCategory Controls { get; } = new("Controls", "grid.svg");
    public static SampleCategory Look { get; } = new("Look and feel", "palette.svg");
    public static SampleCategory Media { get; } = new("Images and camera", "image.svg");
    public static SampleCategory AI { get; } = new("AI and voice", "chat.svg");
    public static SampleCategory Security { get; } = new("Sign-in and security", "lock.svg");
    public static SampleCategory Outside { get; } = new("Outside the app", "bell.svg");

    public static IReadOnlyList<SampleCategory> All { get; } = [Pages, Menus, Controls, Look, Media, AI, Security, Outside];
}
