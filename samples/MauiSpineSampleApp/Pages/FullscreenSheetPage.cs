namespace MauiSpineSampleApp.Pages;

[NavigableSheet(
    Title = "Full screen sheet sample",
    BackgroundPageOverlay = BackgroundPageOverlay.Dimmed,
    AllowedDetents = [SheetDetent.FullScreen])]
public partial class FullscreenSheetPage : INavigableWithResult<FullscreenSheetResult>
{
    public FullscreenSheetPage() => InitializeComponent();
}
