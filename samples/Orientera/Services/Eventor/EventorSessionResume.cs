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
/// Never in a loop, but never only once either. The attempt was started once per app run, and a
/// session that died a second time during the same run was left dead — the runner was told to go
/// and log in under Jag, by an app that had just logged them in by itself ten minutes earlier.
/// Eventor drops sessions after an hour and a half; an app that is open longer than that will see
/// it twice. So: a finished attempt is not a running one, and a session that has expired again is
/// asked for again.
/// </para>
/// <para>
/// The details are saved, so the app logs in every time the session dies. The only thing it will
/// not do is replay details that have already been turned down: those it tries once, then puts
/// Eventor's own page in front of the runner to sort out — which is the one thing that can fix it,
/// and what keeps working the day the federation adds a second factor — and does not try again
/// until the saved details have changed.
/// </para>
/// <para>
/// Two sheets, because the two halves are not the same thing to look at. The replay is the app
/// working and shows a spinner; the page is the runner working and shows the page. Sending a
/// full-screen browser up over whatever they were reading, for a form they were never going to
/// touch, was the app losing its place in front of them.
/// </para>
/// </remarks>
public sealed class EventorSessionResume(
    EventorReader _eventor,
    EventorCredentialStore _credentials)
{
    private Task? _attempt;

    /// <summary>
    /// A fingerprint of details that were tried and did not produce a session.
    /// </summary>
    /// <remarks>
    /// A fingerprint and not the details themselves: this only has to answer "are these the same
    /// ones that failed", and a password is not worth keeping in a field for the life of the app
    /// to answer it. Cleared by a login that works and by any change to what is saved.
    /// </remarks>
    private int? _turnedDown;

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
    /// Revives the session if it has expired and there is a password to replay. One attempt at a
    /// time — everyone who asks while one is running waits for that one — and a new one whenever
    /// the session has expired again since the last.
    /// </summary>
    public Task EnsureAsync(INavigationService navigation)
    {
        if (_attempt is { IsCompleted: true })
            _attempt = null;

        return _attempt ??= AttemptAsync(navigation);
    }

    private async Task AttemptAsync(INavigationService navigation)
    {
        if (await _eventor.AccessAsync() is not EventorAccess.Expired)
            return;

        // Nothing to replay. The runner has to type it once before the app can type it again.
        if (await _credentials.ReadAsync() is not { } saved)
            return;

        // These exact details have already been turned down once. Replaying them would put the
        // same sheet in front of the runner on every page they open.
        if (_turnedDown == saved.GetHashCode())
            return;

        // The quiet attempt first: a spinner and a sentence, with Eventor's page driven out of
        // sight behind it. Nothing on that page needs the runner, so nothing about it needs a
        // screen.
        var quiet = await navigation
            .NavigateToWithResultAsync<EventorResumeSheet, EventorLoginRequest, EventorWebSession>(
                new EventorLoginRequest(UseSavedPassword: true));

        if (quiet.IsSuccess)
        {
            _turnedDown = null;
            Generation++;
            return;
        }

        // It did not get through. Now the page is worth showing, because now there is something on
        // it to do — a refused password, a consent question, or whatever the federation has put in
        // front of the form this week.
        //
        // Opened without the saved password: the app has just sent it and been turned away, and
        // sending it a second time only spends another attempt against an account that may lock.
        var visible = await navigation
            .NavigateToWithResultAsync<EventorLoginSheet, EventorLoginRequest, EventorWebSession>(
                new EventorLoginRequest(UseSavedPassword: false));

        // Only a login that finished changes anything. A cancelled sheet leaves every page reading
        // what it already had.
        if (visible.IsSuccess)
        {
            _turnedDown = null;
            Generation++;
        }
        else
        {
            _turnedDown = saved.GetHashCode();
        }
    }
}
