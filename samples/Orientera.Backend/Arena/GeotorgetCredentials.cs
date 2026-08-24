namespace Orientera.Backend.Arena;

/// <summary>
/// Geotorget-inloggning för Markhöjdmodell Nedladdning. STAC-katalogen är öppen;
/// GeoTIFF:erna bakom kräver kontot.
/// </summary>
/// <remarks>
/// Miljövariabler först — så konfigureras Function-appen — och annars en fil användaren äger
/// och som koden läser men aldrig skriver, samma ordning som prototypen:
/// <c>~/.config/lantmateriet.env</c> med raderna <c>LM_USER=</c> och <c>LM_PASS=</c>.
/// </remarks>
public sealed record GeotorgetCredentials(string User, string Password)
{
    public static GeotorgetCredentials? Find()
    {
        var user = Environment.GetEnvironmentVariable("LM_USER");
        var password = Environment.GetEnvironmentVariable("LM_PASS");
        if (!string.IsNullOrEmpty(user) && !string.IsNullOrEmpty(password))
            return new GeotorgetCredentials(user, password);

        var path = Environment.GetEnvironmentVariable("LM_CREDS")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "lantmateriet.env");
        if (!File.Exists(path))
            return null;

        var values = new Dictionary<string, string>();
        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();
            var split = trimmed.IndexOf('=');
            if (split > 0 && !trimmed.StartsWith('#'))
                values[trimmed[..split].Trim()] = trimmed[(split + 1)..].Trim();
        }
        return values.TryGetValue("LM_USER", out user) && values.TryGetValue("LM_PASS", out password)
            && user.Length > 0 && password.Length > 0
            ? new GeotorgetCredentials(user, password)
            : null;
    }
}
