namespace Orientera.Features.Events;

/// <summary>
/// The landing between the app and Eventor's entry form: what is about to happen, and what goes
/// with you (P11).
/// </summary>
/// <remarks>
/// The heaviest finding in the test run was that the app's own language disappears at the moment
/// that matters most — the runner pressed "Anmäl dig" and landed among banner ads and a cookie
/// bar, with the form itself below the fold. This screen does not fix Eventor's page; it says what
/// is coming, so arriving there is a step the runner took rather than something that happened.
/// </remarks>
[NavigableSheet(
    Title = "Anmälan",
    BackgroundPageOverlay = BackgroundPageOverlay.Dimmed,
    AllowedDetents = [SheetDetent.Medium])]
public partial class EntryHandoffSheet : INavigableWithParameter<EntryHandoff>, INavigableWithResult<bool>
{
    public EntryHandoffSheet() => InitializeComponent();
}
