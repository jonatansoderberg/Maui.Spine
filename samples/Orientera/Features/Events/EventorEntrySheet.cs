using Orientera.Services.Eventor;

namespace Orientera.Features.Events;

/// <summary>
/// Eventor's own entry form, inside the app.
/// </summary>
/// <remarks>
/// Not <c>Launcher.OpenAsync</c>. Safari has its own cookie jar; the Eventor session the runner
/// logged in for lives in the app's web view store, so an entry page opened externally greeted
/// them with "Du behöver vara inloggad för att anmäla dig till en tävling" — measured. Shown here
/// instead, in the same web view store the login wrote to, the page opens already signed in.
/// </remarks>
[NavigableSheet(
    Title = "Anmälan",
    BackgroundPageOverlay = BackgroundPageOverlay.Dimmed,
    AllowedDetents = [SheetDetent.FullScreen],
    InitialDetent = SheetDetent.FullScreen)]
public partial class EventorEntrySheet : INavigableWithParameter<EventorEntry>
{
    public EventorEntrySheet()
    {
        InitializeComponent();

        // Eventor serves its chrome again on every page of the entry flow, so this runs on every
        // navigation rather than once. Both scripts are safe to repeat: the style element is added
        // once, and the class is only set when the form offers it.
        Form.Navigated += async (_, e) =>
        {
            if (e.Result is not WebNavigationResult.Success)
                return;

            await Form.EvaluateJavaScriptAsync(EventorEntryChrome.HideChrome);

            if (BindingContext is EventorEntrySheetViewModel { ClassName.Length: > 0 } vm)
                await Form.EvaluateJavaScriptAsync(EventorEntryChrome.SelectClass(vm.ClassName));
        };
    }
}
