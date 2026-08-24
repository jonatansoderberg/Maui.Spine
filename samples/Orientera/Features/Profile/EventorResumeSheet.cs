using Orientera.Services.Eventor;

namespace Orientera.Features.Profile;

/// <summary>
/// The quiet half of the login: the app replaying a remembered password, with nothing on screen
/// but a spinner and the reason.
/// </summary>
/// <remarks>
/// Same web view and same form as <see cref="EventorLoginSheet"/> — the POST has to be Eventor's
/// own — but a login page nobody is going to type into does not need a screen, and a full-screen
/// browser appearing by itself over whatever the runner was reading looks like the app has lost
/// its place. A quarter-height sheet says what is happening and gets out of the way.
/// <para>
/// It never shows the page it is driving. When the quiet attempt cannot finish,
/// <see cref="EventorSessionResume"/> opens the full sheet afterwards, which is where Eventor's
/// page belongs: in front of somebody who is going to do something about it.
/// </para>
/// </remarks>
[NavigableSheet(
    Title = "Loggar in",
    BackgroundPageOverlay = BackgroundPageOverlay.Dimmed,
    AllowedDetents = [SheetDetent.Compact],
    InitialDetent = SheetDetent.Compact)]
public partial class EventorResumeSheet :
    INavigableWithResult<EventorWebSession>,
    INavigableWithParameter<EventorLoginRequest>
{
    /// <summary>
    /// How long the quiet attempt is given before the runner gets Eventor's page instead.
    /// </summary>
    /// <remarks>
    /// Two navigations over whatever network an arena has, and a consent dialog that is injected
    /// after the page reports itself loaded. Twenty seconds is long enough that a slow morning is
    /// not mistaken for a refusal, and short enough that a spinner nobody can cancel does not
    /// become the app.
    /// </remarks>
    private static readonly TimeSpan GivesUpAfter = TimeSpan.FromSeconds(20);

    /// <summary>Whether the remembered password has already been sent once.</summary>
    private bool _submitted;

    /// <summary>Set the moment the sheet is on its way out, by either door.</summary>
    private bool _settled;

    public EventorResumeSheet()
    {
        InitializeComponent();

        Browser.Navigated += async (_, _) => await OnNavigatedAsync();

        _ = GiveUpLaterAsync();
    }

    private async Task OnNavigatedAsync()
    {
        if (_settled || BindingContext is not EventorLoginSheetViewModel model)
            return;

        // The consent dialog lies over the form and the silent fill clicks straight through it,
        // but it is also what a failed attempt leaves standing in front of the runner (#144).
        await Browser.EvaluateJavaScriptAsync(EventorLoginForm.DeclineConsentScript);
        await Browser.EvaluateJavaScriptAsync(EventorLoginForm.RememberScript);

        var greeting = await Browser.EvaluateJavaScriptAsync(EventorLoginSheetViewModel.LoggedInScript);

        if (await model.OnPageAsync(
                greeting,
                () => EventorCookies.ReadAsync(Browser),
                script => Browser.EvaluateJavaScriptAsync(script)))
        {
            _settled = true;
            return;
        }

        if (await model.CredentialsAsync() is not { } saved)
        {
            await GiveUpAsync(model);
            return;
        }

        if (_submitted)
        {
            // Eventor has answered the POST. No greeting and the login form back on screen is the
            // password being refused — the one failure worth calling immediately, because waiting
            // out the timer would only make a wrong password look like a slow network.
            var form = await Browser.EvaluateJavaScriptAsync(EventorLoginForm.ShowLoginScript);

            if (form?.Contains("shown", StringComparison.Ordinal) == true)
                await GiveUpAsync(model);

            return;
        }

        _submitted = true;

        await Browser.EvaluateJavaScriptAsync(
            EventorLoginForm.FillAndSubmitScript(saved.Username, saved.Password));
    }

    private async Task GiveUpLaterAsync()
    {
        await Task.Delay(GivesUpAfter);

        if (BindingContext is EventorLoginSheetViewModel model)
            await GiveUpAsync(model);
    }

    /// <summary>
    /// Closes with nothing. Guarded because both doors can be reached at once: the timer fires on
    /// its own clock, and closing a sheet that has already closed pops whatever is behind it.
    /// </summary>
    private async Task GiveUpAsync(EventorLoginSheetViewModel model)
    {
        if (_settled)
            return;

        _settled = true;

        await MainThread.InvokeOnMainThreadAsync(model.GiveUpAsync);
    }
}
