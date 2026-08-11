using System.Net;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orientera.Backend.Configuration;

namespace Orientera.Backend.Eventor;

/// <summary>
/// Opens a logged-in Eventor web session for one person.
/// </summary>
/// <remarks>
/// Parts of Eventor exist only as pages behind a login — Sverigelistan's per-runner and per-club
/// lists, and the club activity list. There is no API for either; the ranking area was checked in
/// a browser and makes no XHR calls at all (issue #103).
///
/// The documented way in is <c>externalLoginUrl</c>: with the organisation's API key it mints a
/// one-time link, valid five minutes, that puts a person into logged-in mode. Following it with a
/// cookie jar of our own yields that person's session. No password is involved anywhere.
///
/// Eventor bounds who a session can be minted for: only a member of the organisation whose key is
/// used, 403 for anyone else whichever organisation id is passed. It does not bound what a session
/// can then read, which is why <see cref="RankingOptions.DemoSessionPersonId"/> exists and is a
/// setting rather than a behaviour.
/// </remarks>
public sealed class EventorSession(
    EventorClient _eventor,
    IOptions<EventorOptions> _options,
    ILogger<EventorSession> _logger)
{
    /// <summary>
    /// A client carrying <paramref name="personId"/>'s session, or null if none could be opened.
    /// The caller owns it and must dispose it.
    /// </summary>
    /// <remarks>
    /// One handler and one cookie jar per call. A shared client would mean one person's session
    /// answering another person's request.
    /// </remarks>
    public async Task<HttpClient?> OpenAsync(string personId, CancellationToken cancellationToken)
    {
        var loginUrl = await LoginUrlAsync(personId, cancellationToken);

        if (loginUrl is null)
            return null;

        var handler = new HttpClientHandler { CookieContainer = new CookieContainer() };
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };

        try
        {
            using var login = await http.GetAsync(loginUrl, cancellationToken);

            if (!login.IsSuccessStatusCode)
            {
                http.Dispose();

                return null;
            }

            return http;
        }
        catch
        {
            http.Dispose();

            throw;
        }
    }

    /// <summary>
    /// Asks Eventor for a one-time login link. Answers 403 for anyone outside the organisation
    /// whose key is used, which is the boundary this whole mechanism lives inside.
    /// </summary>
    private async Task<string?> LoginUrlAsync(string personId, CancellationToken cancellationToken)
    {
        if (_options.Value.OrganisationIds is not { Length: > 0 } organisation)
            return null;

        var response = await _eventor.GetAsync(
            "externalLoginUrl",
            new Dictionary<string, string?>
            {
                ["personId"] = personId,
                ["organisationId"] = organisation.Split(',')[0].Trim(),
            },
            cancellationToken);

        if (response is XElement { Value.Length: > 0 } element)
            return element.Value;

        _logger.LogWarning("Eventor gav ingen inloggningslänk för {Person}.", personId);

        return null;
    }
}
