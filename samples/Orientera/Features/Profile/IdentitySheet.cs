namespace Orientera.Features.Profile;

[NavigableSheet(
    Title = "Vem är du?",
    BackgroundPageOverlay = BackgroundPageOverlay.Dimmed,
    AllowedDetents = [SheetDetent.Medium],
    InitialDetent = SheetDetent.Medium)]
public partial class IdentitySheet { public IdentitySheet() => InitializeComponent(); }
