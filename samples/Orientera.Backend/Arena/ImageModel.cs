using System.ClientModel;
using Microsoft.Extensions.Logging;
using OpenAI.Images;

// input_fidelity är märkt som experimentell i SDK:n, men utan den blir terrängen lösare —
// det är hela poängen med redigeringspasset, så varningen är medvetet undertryckt.
#pragma warning disable OPENAI001

namespace Orientera.Backend.Arena;

/// <summary>
/// Bildmodellens pass: den nakna renderingen in, samma scen fotorealistiskt ljussatt ut.
/// </summary>
/// <remarks>
/// Redigering, inte generering — modellen får bilden och instrueras att inte flytta något.
/// <c>input_fidelity=high</c> är det som håller terrängen på plats; avvisar modellen
/// parametern körs passet utan, men det ska synas i loggen i stället för att upptäckas i
/// bilden långt senare.
/// </remarks>
public sealed class ImageModel(ImageClient _client, ILogger<ImageModel> _logger)
{
    public async Task<byte[]> EnhanceAsync(byte[] png, string prompt, CancellationToken cancellationToken)
    {
        // Ingen ResponseFormat: gpt-image-modellerna svarar alltid med bytes och avvisar
        // parametern med 400 om den skickas.
        var options = new ImageEditOptions
        {
            Size = new GeneratedImageSize(ArenaComposer.Width, ArenaComposer.Height),
            InputFidelity = ImageInputFidelity.High,
        };
        try
        {
            return await EditAsync(png, prompt, options, cancellationToken);
        }
        // gpt-image-2 avvisar input_fidelity med 400, inte med något typfel.
        catch (ClientResultException exception) when (exception.Message.Contains("input_fidelity"))
        {
            _logger.LogWarning("input_fidelity stöds inte av modellen — kör utan. Terrängen blir lösare.");
            options.InputFidelity = null;
            return await EditAsync(png, prompt, options, cancellationToken);
        }
    }

    /// <summary>Ren generering ur text, för inomhusbilderna som inte har någon terräng bakom sig.</summary>
    public async Task<byte[]> GenerateAsync(string prompt, CancellationToken cancellationToken)
    {
        var result = await _client.GenerateImageAsync(prompt, new ImageGenerationOptions
        {
            Size = new GeneratedImageSize(ArenaComposer.Width, ArenaComposer.Height),
        }, cancellationToken);
        return result.Value.ImageBytes.ToArray();
    }

    private async Task<byte[]> EditAsync(
        byte[] png, string prompt, ImageEditOptions options, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(png);
        var result = await _client.GenerateImageEditAsync(stream, "render.png", prompt, options, cancellationToken);
        return result.Value.ImageBytes.ToArray();
    }

    /// <summary>
    /// Nyckeln ur miljön — så konfigureras Function-appen — eller ur en fil användaren äger
    /// och som koden läser men aldrig skriver: <c>~/.config/openai.env</c>.
    /// </summary>
    public static string? FindApiKey()
    {
        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrEmpty(key))
            return key;

        var path = Environment.GetEnvironmentVariable("OPENAI_CREDS")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "openai.env");
        if (!File.Exists(path))
            return null;
        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith("OPENAI_API_KEY=", StringComparison.Ordinal))
                return line["OPENAI_API_KEY=".Length..].Trim();
        }
        return null;
    }
}
