using Orientera.Features.Profile;

namespace Orientera.Services.Eventor;

/// <summary>
/// Logs the runner back in to Eventor when it has forgotten them, if they let the app remember.
/// </summary>
/// <remarks>
/// Measured twice on #123: Eventor issues a session cookie with no expiry and "kom ihåg mig" adds
/// nothing, so the login dies when the server drops it — after two days once, after an hour and a
/// half the next time. Nothing about that is the runner's fault and nothing about it is worth a
/// screen, so the app quietly does again what it did the first time.
/// <para>
/// This lived on Hem, which meant it only ever ran for someone who opened Hem. A session that died
/// while the runner was reading results stayed dead: the results list emptied, Sverigelistan
/// vanished, the entry page said "du behöver vara inloggad" and the start field lost its ranking —
/// four pages each telling a different half-truth about one fact. Any page that reads Eventor can
/// ask for the resume now, and the first one to ask does it for all of them.
/// </para>
/// <para>
/// Once per app run and never in a loop: if the saved password no longer works, the sheet is left
/// standing open on Eventor's own page for the runner to sort out, which is the one thing that can
/// actually fix it. That is also what keeps working the day the federation adds a second factor.
/// </para>
/// </remarks>
public sealed class EventorSessionResume(
    EventorReader _eventor,
    EventorCredentialStore _credentials)
{
    private bool _tried;

    /// <summary>
    /// Revives the session if it has expired and there is a password to replay.
    /// </summary>
    /// <returns>True when a login was attempted, so the caller knows to read again.</returns>
    public async Task<bool> TryResumeAsync(INavigationService navigation)
    {
        if (_tried)
            return false;

        _tried = true;

        if (await _eventor.AccessAsync() is not EventorAccess.Expired)
            return false;

        if (await _credentials.ReadAsync() is null)
            return false;

        await navigation.NavigateToWithResultAsync<EventorLoginSheet, EventorLoginRequest, EventorWebSession>(
            new EventorLoginRequest(UseSavedPassword: true));

        return true;
    }
}
