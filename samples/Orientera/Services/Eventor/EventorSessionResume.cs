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
    private Task? _attempt;

    /// <summary>
    /// Which session the app is on. Counts up when a login has replaced the one before it.
    /// </summary>
    /// <remarks>
    /// The answer used to be a bool returned to whoever asked, and only one page could get it:
    /// Hem is realized first and reached the ask within a second of start, so the page the runner
    /// was actually looking at was told "no" and kept the list it had read with the dead session
    /// (#140). Whether a page needs to read again is not about who triggered the login — it is
    /// about whether the session it read with is still the app's, and that is a question every
    /// page can answer for itself.
    /// </remarks>
    public int Generation { get; private set; }

    /// <summary>
    /// Revives the session if it has expired and there is a password to replay. Started once per
    /// app run; everyone who asks after that waits for the same attempt.
    /// </summary>
    public Task EnsureAsync(INavigationService navigation) => _attempt ??= AttemptAsync(navigation);

    private async Task AttemptAsync(INavigationService navigation)
    {
        if (await _eventor.AccessAsync() is not EventorAccess.Expired)
            return;

        if (await _credentials.ReadAsync() is null)
            return;

        var session = await navigation
            .NavigateToWithResultAsync<EventorLoginSheet, EventorLoginRequest, EventorWebSession>(
                new EventorLoginRequest(UseSavedPassword: true));

        // Only a login that finished changes anything. A cancelled sheet, or one left standing
        // open because the password no longer works, leaves every page reading what it already had.
        if (session.IsSuccess)
            Generation++;
    }
}
