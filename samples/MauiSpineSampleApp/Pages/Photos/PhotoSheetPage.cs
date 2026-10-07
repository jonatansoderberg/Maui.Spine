namespace MauiSpineSampleApp.Pages.Photos;

// The grid runs to the sheet's bottom edge and scrolls its last row clear of the home indicator,
// rather than stopping above it with the photos cut off there.
[NavigableSheet(
    Title = "Photos",
    AllowedDetents = [SheetDetent.Medium, SheetDetent.FullScreen],
    SafeAreaEdges = SafeAreaEdges.Top | SafeAreaEdges.Left | SafeAreaEdges.Right,
    ScrollInset = SafeAreaEdges.Bottom)]
public partial class PhotoSheetPage { public PhotoSheetPage() => InitializeComponent(); }
