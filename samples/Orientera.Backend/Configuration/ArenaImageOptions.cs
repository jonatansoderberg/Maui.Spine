namespace Orientera.Backend.Configuration;

/// <summary>Var arenabilderna ligger, och vilken generation av dem som gäller.</summary>
/// <remarks>
/// Bilderna kostar pengar att skapa och ingenting att lagra, så de görs en gång och sparas —
/// terrängen ändrar sig inte mellan två tävlingar på samma plats. Blobben är därför inte en
/// cache med livslängd utan ett arkiv, och det som avgör om en bild fortfarande gäller är
/// <see cref="Version"/>.
/// </remarks>
public sealed class ArenaImageOptions
{
    public const string Section = "ArenaImage";

    /// <summary>Saknas den är funktionen av, och appen får veta det i stället för att gissa.</summary>
    public string? ConnectionString { get; set; }

    public string Container { get; set; } = "arenabilder";

    /// <summary>
    /// Ingår i varje bloburl. Renderare och prompt utvecklas — höjs den inte när de ändras
    /// serveras gamla bilder tyst vidare, och skillnaden syns inte förrän någon jämför två
    /// tävlingar sida vid sida och undrar varför de inte ser likadana ut.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Hur länge svaret om en bild får återanvändas. Kort, för det som ändras är inte bilden
    /// utan huruvida den hunnit bli till.
    /// </summary>
    public int LookupMinutes { get; set; } = 5;

    /// <summary>
    /// Kön som beställer nya bilder. Renderingen kan inte ske här — höjddata ska hämtas,
    /// terrängen renderas och bilden gå genom en bildmodell, tillsammans dryga minuten — så
    /// backend lägger en beställning och svarar att bilden inte finns än.
    /// </summary>
    public string Queue { get; set; } = "arenabilder-att-gora";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString);
}
