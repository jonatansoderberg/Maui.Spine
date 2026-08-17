using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Orientera.Backend.Caching;
using Orientera.Backend.Eventor;
using Orientera.Domain;

namespace Orientera.Backend.Ranking;

/// <summary>
/// Who is entered in a class, with the club each of them runs for.
/// </summary>
/// <remarks>
/// Names, person ids and clubs come from Eventor's API with the club's own key, which is a key for
/// data and not for a person. Sverigelistan's points do not come from here at all any more: the
/// club pages are behind a personal login, and since #123 that login is the reader's own and the
/// pages are read on their phone. A server reading them would be one member's subscription
/// answering for everybody.
///
/// So this hands over the field in start order and states each runner's club id. The phone adds the
/// points, or does not, and the list is honest either way.
/// </remarks>
public sealed class StartFieldSource(
    EventorClient _eventor,
    ResponseCache _cache,
    ILogger<StartFieldSource> _logger)
{
    public Task<IReadOnlyList<StartFieldRunner>> ForClassAsync(
        string eventId, string className, CancellationToken cancellationToken) =>
        _cache.GetOrAddAsync(
            $"field:{eventId}:{className}",
            TimeSpan.FromHours(1),
            token => FetchAsync(eventId, className, token),
            cancellationToken);

    private async Task<IReadOnlyList<StartFieldRunner>> FetchAsync(
        string eventId, string className, CancellationToken cancellationToken)
    {
        try
        {
            var starts = await _eventor.GetAsync(
                "starts/event",
                new Dictionary<string, string?> { ["eventId"] = eventId },
                cancellationToken);

            return [.. Field(starts, className).OrderBy(r => r.StartTime ?? DateTimeOffset.MaxValue)];
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(exception, "Startfältet kunde inte hämtas för {Event} {Class}.", eventId, className);

            return [];
        }
    }

    /// <summary>The runners in one class, as the start list states them, with their club's id.</summary>
    internal static List<StartFieldRunner> Field(XElement starts, string className)
    {
        var field = new List<StartFieldRunner>();

        foreach (var classStart in starts.Deep("ClassStart"))
        {
            var name = classStart.Child("EventClass").Text("Name")
                ?? classStart.Child("EventClass").Text("ClassShortName");

            if (!string.Equals(name, className, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var personStart in classStart.Children("PersonStart"))
            {
                var person = personStart.Child("Person");

                if (person.Text("PersonId") is not { Length: > 0 } id)
                    continue;

                var given = string.Join(' ', person.Child("PersonName").Children("Given").Select(g => g.Value.Trim()));
                var family = person.Child("PersonName").Text("Family") ?? string.Empty;

                field.Add(new StartFieldRunner
                {
                    Person = new PersonId(id),
                    Name = string.Join(' ', new[] { given, family }.Where(p => p.Length > 0)),
                    Club = personStart.Child("Organisation").Text("Name") ?? string.Empty,
                    ClubId = personStart.Child("Organisation").Text("OrganisationId"),
                    StartTime = personStart.Child("Start").Child("StartTime").Moment(TimeZoneInfo.Local),
                });
            }
        }

        return field;
    }
}
