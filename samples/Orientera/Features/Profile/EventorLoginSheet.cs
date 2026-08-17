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
public partial class EventorLoginSheet :
    INavigableWithResult<EventorWebSession>,
    INavigableWithParameter<EventorLoginRequest>
{
    public EventorLoginSheet()
    {
        InitializeComponent();

        Browser.Navigated += async (_, _) =>
        {
            if (BindingContext is not EventorLoginSheetViewModel model)
                return;

            // Hooked on every page, because the login form is not always the first one shown and
            // the values have to be caught as they are submitted — afterwards the page is gone.
            await Browser.EvaluateJavaScriptAsync(EventorLoginForm.RememberScript);

            // The form is most of a screen below the fold on Eventor's own page.
            await Browser.EvaluateJavaScriptAsync(EventorLoginForm.ShowLoginScript);

            if (model.WantsSilentLogin && await model.CredentialsAsync() is { } saved)
            {
                await Browser.EvaluateJavaScriptAsync(
                    EventorLoginForm.FillAndSubmitScript(saved.Username, saved.Password));
            }

            var greeting = await Browser.EvaluateJavaScriptAsync(EventorLoginSheetViewModel.LoggedInScript);

            await model.OnPageAsync(
                greeting,
                () => EventorCookies.ReadAsync(Browser),
                script => Browser.EvaluateJavaScriptAsync(script));
        };
    }
}
