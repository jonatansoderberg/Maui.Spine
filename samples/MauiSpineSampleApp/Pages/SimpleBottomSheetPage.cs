namespace MauiSpineSampleApp.Pages;

[NavigableSheet(
    Title = "Simple bottom sheet", 
    BackgroundPageOverlay = BackgroundPageOverlay.Blurred, 
    Lifetime = ServiceLifetime.Singleton)]
public partial class SimpleBottomSheetPage { public SimpleBottomSheetPage() => InitializeComponent(); }