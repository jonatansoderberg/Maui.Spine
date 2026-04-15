namespace MauiSpineSampleApp.Pages;

[NavigableSheet(
    Title = "Small sheet sample",
    BackgroundPageOverlay = BackgroundPageOverlay.Dimmed,
    AllowedDetents = [SheetDetent.Compact])]
public partial class SmallSheetPage { public SmallSheetPage() => InitializeComponent(); }
