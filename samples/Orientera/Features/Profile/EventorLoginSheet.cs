using Orientera.Services.Eventor;

namespace Orientera.Features.Profile;

/// <summary>
/// Full screen, because it is Eventor's own login page and it needs the room a login page needs.
/// </summary>
/// <remarks>
/// The cookie reading lives here rather than in the view model: the session cookie is HttpOnly and
/// only the platform's web view store can see it, and that store is reached through this page's
/// handler.
/// </remarks>
[NavigableSheet(
    Title = "Logga in på Eventor",
    BackgroundPageOverlay = BackgroundPageOverlay.Dimmed,
    AllowedDetents = [SheetDetent.FullScreen],
    InitialDetent = SheetDetent.FullScreen)]
public partial class EventorLoginSheet : INavigableWithResult<EventorWebSession>
{
    public EventorLoginSheet()
    {
        InitializeComponent();

        Browser.Navigated += async (_, _) =>
        {
            if (BindingContext is not EventorLoginSheetViewModel model)
                return;

            var greeting = await Browser.EvaluateJavaScriptAsync(EventorLoginSheetViewModel.LoggedInScript);

            await model.OnPageAsync(greeting, () => EventorCookies.ReadAsync(Browser));
        };
    }
}
