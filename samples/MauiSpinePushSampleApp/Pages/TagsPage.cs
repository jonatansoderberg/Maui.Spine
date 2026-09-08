namespace MauiSpinePushSampleApp.Pages;

// Opens full screen, not at Medium: the sheet lays its content out against the full screen height,
// so a Spara pinned to the bottom of the page sits below the Medium edge and cannot be reached
// without dragging the sheet up first.
[NavigableSheet(
    Title = "Taggar",
    InitialDetent = SheetDetent.FullScreen,
    AllowedDetents = [SheetDetent.Medium, SheetDetent.FullScreen])]
public partial class TagsPage { public TagsPage() => InitializeComponent(); }
