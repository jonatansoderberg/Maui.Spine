namespace MauiSpineSampleApp.Pages.Keyboard;

[NavigableRegion(Title = "Keyboard")]
public partial class KeyboardPage { public KeyboardPage() => InitializeComponent(); }

[NavigableSheet(
    Title = "Keyboard in a sheet",
    AllowedDetents = [SheetDetent.Medium, SheetDetent.FullScreen])]
public partial class KeyboardSheetPage { public KeyboardSheetPage() => InitializeComponent(); }
