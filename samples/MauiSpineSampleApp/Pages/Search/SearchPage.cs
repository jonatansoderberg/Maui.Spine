namespace MauiSpineSampleApp.Pages.Search;

[NavigableRegion(Title = "Search")]
public partial class SearchPage { public SearchPage() => InitializeComponent(); }

[NavigableSheet(
    Title = "Search in a sheet",
    AllowedDetents = [SheetDetent.Medium, SheetDetent.FullScreen])]
public partial class SearchSheetPage { public SearchSheetPage() => InitializeComponent(); }
