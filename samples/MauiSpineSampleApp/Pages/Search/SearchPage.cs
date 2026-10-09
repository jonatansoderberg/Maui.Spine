namespace MauiSpineSampleApp.Pages.Search;

// The window's title bar (Windows only), which the field goes in; the other sample pages hide it.
[NavigableRegion(Title = "Search", IsTitleBarVisible = true)]
public partial class SearchPage { public SearchPage() => InitializeComponent(); }

[NavigableSheet(
    Title = "Search in a sheet",
    AllowedDetents = [SheetDetent.Medium, SheetDetent.FullScreen])]
public partial class SearchSheetPage { public SearchSheetPage() => InitializeComponent(); }
