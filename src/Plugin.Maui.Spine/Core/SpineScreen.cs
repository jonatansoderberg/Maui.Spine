using Plugin.Maui.Spine.Presentation;
using Plugin.Maui.Spine.Services;

namespace Plugin.Maui.Spine.Core;

/// <summary>A page on screen, as <see cref="SpineScreen.ShownPages"/> lists it.</summary>
/// <param name="Page">The page.</param>
/// <param name="InSheet">Whether the page is in the open sheet rather than in the root region.</param>
/// <param name="CanGoBack">
/// Whether <see cref="INavigationService.BackAsync"/> would leave this page now: it is the topmost page,
/// and it sits above another page or is in a sheet, which going back from its first page closes.
/// </param>
public sealed record ShownPage(View Page, bool InSheet, bool CanGoBack);

/// <summary>
/// The app's window as a whole: which pages are on screen, when the app can first show something of its
/// own, and whether the window is kept out of screenshots and recordings. For a package that acts on
/// the whole screen, such as an app lock.
/// </summary>
public static partial class SpineScreen
{
    private static IReadOnlyList<ShownPage> _raised = [];

    /// <summary>
    /// The pages on screen: the current page of the root region (the selected tab's, with tabs), then the
    /// current page of the sheet while one is open. During a push or a pop, and under a lightbox, the page
    /// behind is listed too.
    /// </summary>
    public static IReadOnlyList<ShownPage> ShownPages =>
        IPlatformApplication.Current?.Services.GetService<SpineHostProvider>()?.Current is { } host ? Collect(host) : [];

    /// <summary>Raised on the main thread when <see cref="ShownPages"/> has changed.</summary>
    public static event EventHandler? ShownPagesChanged;

    /// <summary>
    /// Completes once the window has been active and the first root page is in place: the earliest an
    /// app can show a prompt of its own on a cold start.
    /// </summary>
    public static Task WhenReady
    {
        get
        {
            var readiness = IPlatformApplication.Current?.Services.GetService<SpineReadiness>()
                ?? throw new InvalidOperationException("SpineScreen.WhenReady needs a Spine app: call UseSpine() in MauiProgram.");

            return Task.WhenAll(readiness.WhenActivated, readiness.WhenRootSet);
        }
    }

    /// <summary>
    /// Keeps the app's windows out of screenshots, screen recordings and the app switcher's thumbnail where
    /// the platform allows it. Android sets <c>FLAG_SECURE</c> on the activity, on an open sheet and on
    /// overlays; Windows excludes the window from capture (Windows 10 2004 and later). iOS and Mac Catalyst
    /// cannot block a screenshot, so there it does nothing.
    /// </summary>
    public static bool ExcludeFromCapture
    {
        get;
        set
        {
            if (field == value)
                return;

            field = value;
            ApplyCaptureExclusion();
        }
    }

    /// <summary>Raises <see cref="ShownPagesChanged"/> when the pages on screen differ from the last time it did.</summary>
    internal static void Update()
    {
        var shown = ShownPages;
        if (shown.SequenceEqual(_raised))
            return;

        _raised = shown;
        ShownPagesChanged?.Invoke(null, EventArgs.Empty);
    }

    private static List<ShownPage> Collect(ISpineHost host)
    {
        var root = (NavigationRegionViewModel)host.RootNavigationRegion.BindingContext;
        var active = host.ActiveRegionViewModel;
        var inSheet = !ReferenceEquals(active, root);
        var pages = new List<ShownPage>(3);

        if (root.BackView.Content is { } behind)
            pages.Add(new(behind, false, false));
        if (root.FrontView.Content is { } page)
            pages.Add(new(page, false, !inSheet && root.BackEnabled()));

        if (inSheet)
        {
            if (active.BackView.Content is { } sheetBehind)
                pages.Add(new(sheetBehind, true, false));
            if (active.FrontView.Content is { } sheetPage)
                pages.Add(new(sheetPage, true, true));
        }

        return pages;
    }

    static partial void ApplyCaptureExclusion();
}
