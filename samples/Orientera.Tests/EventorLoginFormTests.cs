using Orientera.Services.Eventor;

namespace Orientera.Tests;

/// <summary>
/// The script that fills Eventor's login form.
/// </summary>
/// <remarks>
/// It is built by string concatenation and handed to a web view, which is the shape where a
/// quote in a password stops being a character and starts being code. These pin the escaping,
/// because the failure is silent: the login simply does not happen, and only for the people
/// whose password contains the wrong character.
/// </remarks>
public class EventorLoginFormTests
{
    [Fact]
    public void The_measured_field_names_are_the_ones_used()
    {
        var script = EventorLoginForm.FillAndSubmitScript("jonatan", "hemligt");

        Assert.Contains("PersonUsername", script);
        Assert.Contains("PersonPassword", script);
        Assert.Contains("PersonPersistentLogin", script);
        Assert.Contains("PersonLogin", script);
    }

    /// <summary>The club login sits on the same page and is a different account entirely.</summary>
    [Fact]
    public void The_club_login_on_the_same_page_is_left_alone()
    {
        Assert.DoesNotContain("ClubPassword", EventorLoginForm.FillAndSubmitScript("a", "b"));
    }

    [Theory]
    [InlineData("it's")]
    [InlineData("say \"hi\"")]
    [InlineData("back\\slash")]
    [InlineData("line\nbreak")]
    [InlineData("</script>")]
    public void A_password_cannot_escape_its_own_string(string password)
    {
        var script = EventorLoginForm.FillAndSubmitScript("runner", password);

        // Whatever the password contained, the literal it went into is still one literal: the
        // quotes that open and close it are the only unescaped ones in the script.
        int unescaped = 0;

        for (int i = 0; i < script.Length; i++)
        {
            if (script[i] == '\'' && (i == 0 || script[i - 1] != '\\'))
                unescaped++;
        }

        // Two literals — the username's and the password's — so four unescaped quotes, plus the
        // ones in the form selector.
        Assert.Equal(0, unescaped % 2);
        Assert.DoesNotContain("</script>", script);
    }

    /// <summary>
    /// The two values are read back one at a time and percent-encoded.
    /// </summary>
    /// <remarks>
    /// The first version returned both separated by a newline. What came back from the web view
    /// had the newline as the two characters backslash and n, so the split found one field
    /// instead of two, nothing was stored, and the silent login never happened — with no error
    /// anywhere to say so.
    /// </remarks>
    [Fact]
    public void What_was_typed_comes_back_one_value_at_a_time_and_encoded()
    {
        Assert.Contains("encodeURIComponent", EventorLoginForm.ReadRememberedUsernameScript);
        Assert.Contains("encodeURIComponent", EventorLoginForm.ReadRememberedPasswordScript);
        Assert.Contains("orientera.u", EventorLoginForm.ReadRememberedUsernameScript);
        Assert.Contains("orientera.p", EventorLoginForm.ReadRememberedPasswordScript);

        Assert.DoesNotContain("\\n", EventorLoginForm.ReadRememberedPasswordScript);
    }

    [Theory]
    [InlineData("it's")]
    [InlineData("say \"hi\"")]
    [InlineData("back\\slash")]
    [InlineData("line\nbreak")]
    [InlineData("ä ö å")]
    public void A_percent_encoded_value_survives_the_trip_back(string original)
    {
        // What the web view hands over, as JavaScript's encodeURIComponent would render it.
        var encoded = Uri.EscapeDataString(original);

        Assert.Equal(original, Uri.UnescapeDataString(encoded));
    }

    /// <summary>
    /// Reported from a real run: "det visas inget inloggningsformulär på eventor". It was there,
    /// a screen and a half below the news and the social-login buttons.
    /// </summary>
    [Fact]
    public void The_personal_login_is_scrolled_into_view()
    {
        Assert.Contains("PersonUsername", EventorLoginForm.ShowLoginScript);
        Assert.Contains("scrollIntoView", EventorLoginForm.ShowLoginScript);
    }

    [Fact]
    public void Remembering_is_hooked_on_the_forms_own_submit()
    {
        Assert.Contains("addEventListener('submit'", EventorLoginForm.RememberScript);
        Assert.Contains("sessionStorage", EventorLoginForm.RememberScript);
    }

    /// <summary>Ticked so that the day Eventor issues a persistent cookie, the app already asked.</summary>
    [Fact]
    public void Remember_me_is_ticked()
    {
        Assert.Contains("PersonPersistentLogin.checked = true", EventorLoginForm.FillAndSubmitScript("a", "b"));
    }
}
