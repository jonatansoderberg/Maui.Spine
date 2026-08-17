namespace Orientera.Services.Eventor;

/// <summary>
/// Eventor's own login form, as the app drives it.
/// </summary>
/// <remarks>
/// One place for the field names, because both ways into the app go through this form. Whether the
/// runner types into Eventor's page themselves, types into a form of ours, or types nothing at all
/// and a remembered password is replayed, what reaches Eventor is the same POST from the same page
/// — which is what keeps working when the federation puts Cloudflare's challenge in front of it,
/// and what will keep working the day they add a second factor.
///
/// Measured on the live page: <c>PersonUsername</c>, <c>PersonPassword</c>,
/// <c>PersonPersistentLogin</c> and the <c>PersonLogin</c> submit, inside <c>form[action="/Login"]</c>.
/// The club login on the same page uses <c>Club*</c> and is not this.
/// </remarks>
public static class EventorLoginForm
{
    public const string Url = $"{EventorSite.Origin}/Login";

    private const string Form = "document.querySelector('form[action=\"/Login\"]')";

    /// <summary>
    /// Remembers what was typed, at the moment it is submitted.
    /// </summary>
    /// <remarks>
    /// Hooked onto the form's own submit rather than read off the fields afterwards: by the time
    /// the app is told the page changed, the page carrying the fields is gone. The values are put
    /// in <c>sessionStorage</c>, which survives the navigation and dies with the web view.
    ///
    /// This is the line where the app starts seeing the password, so it is worth being plain about
    /// it: without this, nothing can be replayed and Eventor logs the runner out within hours.
    /// What is remembered goes to the platform's secure store and never anywhere else.
    /// </remarks>
    public const string RememberScript = $$"""
        (function () {
            var f = {{Form}};
            if (!f || f.dataset.orientera) return '';
            f.dataset.orientera = '1';
            f.addEventListener('submit', function () {
                try {
                    sessionStorage.setItem('orientera.u', f.PersonUsername.value);
                    sessionStorage.setItem('orientera.p', f.PersonPassword.value);
                } catch (e) { }
            });
            return '';
        })()
        """;

    /// <summary>What was typed into the username field, percent-encoded. Empty before any submit.</summary>
    public static string ReadRememberedUsernameScript => Remembered("orientera.u");

    /// <summary>What was typed into the password field, percent-encoded.</summary>
    public static string ReadRememberedPasswordScript => Remembered("orientera.p");

    /// <summary>
    /// Reads one remembered value back out, percent-encoded.
    /// </summary>
    /// <remarks>
    /// Encoded, and read one value at a time, because what comes back from a web view is a
    /// JavaScript value rendered as a string — quoted on one platform, escaped differently on
    /// another. The first attempt returned both values separated by a newline and the newline
    /// arrived as the two characters backslash and n, so the split found nothing, nothing was
    /// saved, and the silent login stayed silent. Percent-encoding leaves only characters that
    /// survive every one of those layers unchanged.
    /// </remarks>
    private static string Remembered(string key) => $$"""
        (function () {
            try { return encodeURIComponent(sessionStorage.getItem('{{key}}') || ''); }
            catch (e) { return ''; }
        })()
        """;

    /// <summary>
    /// Scrolls Eventor's personal login into view and puts the cursor in it.
    /// </summary>
    /// <remarks>
    /// The page opens on federation news, two social-login buttons and a consent widget; the box
    /// for a personal username and password is most of a screen further down. On a phone that
    /// reads as a page with no login form on it, which is what it was reported as.
    ///
    /// Scrolling rather than hiding the rest: it is Eventor's page and the app has no business
    /// rewriting it, only pointing at the part the runner came for.
    /// </remarks>
    public const string ShowLoginScript = $$"""
        (function () {
            var f = {{Form}};
            if (!f || !f.PersonUsername) return 'no-form';
            f.PersonUsername.scrollIntoView({ block: 'center' });
            return 'shown';
        })()
        """;

    /// <summary>
    /// Fills the form and submits it, the way a finger would.
    /// </summary>
    /// <remarks>
    /// The events matter. A value assigned straight to <c>.value</c> is invisible to the page's own
    /// scripts, and Eventor's form watches its fields; dispatching input and change makes the page
    /// believe what it is being told. "Kom ihåg mig" is ticked because there is no reason not to —
    /// measured on #123 it adds no cookie at all, but the day Eventor starts issuing one, this is
    /// already asking for it.
    /// </remarks>
    public static string FillAndSubmitScript(string username, string password) => $$"""
        (function () {
            var f = {{Form}};
            if (!f) return 'no-form';

            function set(field, value) {
                field.value = value;
                field.dispatchEvent(new Event('input', { bubbles: true }));
                field.dispatchEvent(new Event('change', { bubbles: true }));
            }

            set(f.PersonUsername, {{Literal(username)}});
            set(f.PersonPassword, {{Literal(password)}});

            if (f.PersonPersistentLogin) f.PersonPersistentLogin.checked = true;

            (f.PersonLogin || f).click ? f.PersonLogin.click() : f.submit();
            return 'submitted';
        })()
        """;

    /// <summary>A JavaScript string literal that cannot end early, whatever is in the password.</summary>
    private static string Literal(string value)
    {
        var escaped = value
            .Replace("\\", "\\\\")
            .Replace("'", "\\'")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("<", "\\x3c");

        return $"'{escaped}'";
    }
}
