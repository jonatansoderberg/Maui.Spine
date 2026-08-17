using System.Security.Cryptography;
using System.Text;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orientera.Backend.Caching;
using Orientera.Backend.Configuration;
using Orientera.Services.Sources;

namespace Orientera.Backend.Story;

/// <summary>
/// Turns the facts of a race into a few sentences a runner would want to read.
/// </summary>
/// <remarks>
/// The division of labour is deliberate and one-way: the app decides <em>what is true</em>, this
/// decides <em>how it sounds</em>. Nothing here may add a fact, and the prompt says so more than
/// once, because an encouraging tone is exactly the pressure that invents a good stretch that
/// never happened. What the model is actually good at — turning six flat statements into a
/// paragraph that sounds like a coach who watched — is all it is asked for.
/// </remarks>
public sealed class RaceStoryWriter(
    IOptions<StoryOptions> _options,
    ResponseCache _cache,
    ILogger<RaceStoryWriter>? _logger = null)
{
    private const string Coach = """
        Du är en erfaren orienteringstränare som sammanfattar ett lopp för löparen själv, på svenska.

        Du får en punktlista med fakta om loppet. Skriv om dem till löpande text i du-form, i
        förfluten tid.

        Regler:
        - Använd bara fakta ur listan. Lägg aldrig till en kontroll, en tid, en placering eller
          en händelse som inte står där. Om något saknas, skriv inte om det.
        - 2–4 meningar, men aldrig fler meningar än listan har punkter. Är listan kort blir
          texten kort, och då väger flera punkter in i samma mening. Fyll aldrig ut.
        - Skriv ingenting om hur löparen låg till under loppet, om farten var jämn eller ojämn,
          eller hur banan var, om inte listan säger det. Två punkter om start och målgång säger
          ingenting om vad som hände däremellan.
        - Bind ihop punkterna med språket, inte med påståenden. "Sedan vände det" och "därefter
          lossnade det" är påståenden om sträckor du inte fått veta något om.
        - Siffror och kontrollnummer ska återges exakt som de står. Numrera aldrig om, och skriv
          aldrig "sträcka 3" om en kontroll — listan säger "kontroll 3", och så heter den.
        - Behåll listans förbehåll: ett uppskattat tapp är uppskattat, inte uppmätt.
        - Tonen är positiv och peppande, men ärlig — och peppet får aldrig bli utfyllnad. En bom
          nämns som det den är, gärna med vad som går att vinna nästa gång. Skönmåla inte.
        - Ingen rubrik, inga punktlistor, inga emojis. Bara texten.
        """;

    public async Task<RaceStory?> WriteAsync(RaceStoryRequest request, CancellationToken cancellationToken)
    {
        var options = _options.Value;

        if (!options.IsConfigured || request.Lines.Count == 0)
            return null;

        try
        {
            return await _cache.GetOrAddAsync(
                $"story:{options.Model}:{Fingerprint(request)}",
                TimeSpan.FromHours(options.CacheHours),
                token => AskAsync(request, options, token),
                cancellationToken);
        }
        // Ett uteblivet svar visas som inget kort alls, vilket ser likadant ut som "ingen nyckel
        // konfigurerad". Utan den här raden skiljer ingenting de två åt i efterhand.
        catch (AnthropicException exception)
        {
            _logger?.LogWarning(exception, "Loppberättelsen kunde inte skrivas ({Model}).", options.Model);

            return null;
        }
    }

    private static async Task<RaceStory?> AskAsync(
        RaceStoryRequest request,
        StoryOptions options,
        CancellationToken cancellationToken)
    {
        AnthropicClient client = new() { ApiKey = options.ApiKey };

        var facts = new StringBuilder()
            .AppendLine($"Klass: {request.Class}")
            .AppendLine()
            .AppendLine("Fakta om loppet:");

        foreach (var line in request.Lines)
            facts.AppendLine($"- {line}");

        var message = await client.Messages.Create(
            new MessageCreateParams
            {
                Model = options.Model,
                MaxTokens = 1024,
                System = new List<TextBlockParam> { new() { Text = Coach } },
                // Phrasing known facts is not a reasoning task; effort spent here is latency the
                // runner waits through for the same paragraph.
                OutputConfig = new OutputConfig { Effort = Effort.Low },
                Messages = [new() { Role = Role.User, Content = facts.ToString() }],
            },
            cancellationToken: cancellationToken);

        var text = string.Concat(message.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text)).Trim();

        return text.Length == 0 ? null : new RaceStory { Text = text };
    }

    /// <summary>
    /// The cache key is the facts themselves — two runners with identical races are vanishingly
    /// rare, and a race that is reopened is the same race. Hashed rather than joined so the key
    /// stays short and carries no names.
    /// </summary>
    private static string Fingerprint(RaceStoryRequest request)
    {
        var payload = $"{request.Class}\n{string.Join('\n', request.Lines)}";

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))[..16];
    }
}
