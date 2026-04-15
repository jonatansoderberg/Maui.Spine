namespace MauiSpineSampleApp.Pages;

[NavigableSheet(Title = "Sample bottom sheet", BackgroundPageOverlay = BackgroundPageOverlay.Dimmed, AllowedDetents = [SheetDetent.Medium, SheetDetent.FullScreen, "75%"])]
public partial class SamplePage { public SamplePage() => InitializeComponent(); }