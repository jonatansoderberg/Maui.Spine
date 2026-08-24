using System.Text.Json;

namespace Orientera.Tests;

/// <summary>
/// Facit för arenabilderna: <c>tools/arenabild/referens/checkpoints.json</c>. Toleranserna
/// står i filen, inte i testerna.
///
/// Måtten kommer från Python-prototypen som porten mättes mot. Prototypen är borta — porten
/// är implementationen nu — men facit står kvar som regressionsskydd: ändras renderaren så
/// att kantkorrelationen faller, ska det märkas.
/// </summary>
internal static class ArenaFacit
{
    public static string RepoRoot { get; } = FindRepoRoot();

    public static readonly Lazy<JsonElement> Checkpoints = new(() =>
        JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(
            Path.Combine(RepoRoot, "tools", "arenabild", "referens", "checkpoints.json"))));

    /// <summary>Nedladdade höjdrutor — det är de som gör facittesterna nätfria.</summary>
    public static string ElevationCache => Path.Combine(RepoRoot, "tools", "arenabild", "cache", "hojd");

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Spine.slnx")))
                return dir.FullName;
        }
        throw new InvalidOperationException("hittade inte solutionroten ovanför " + AppContext.BaseDirectory);
    }

    /// <summary>Ett facittest som behöver de cachade höjdrutorna, och hoppas över där de saknas.</summary>
    public sealed class ElevationFactAttribute : FactAttribute
    {
        public ElevationFactAttribute()
        {
            if (!Directory.Exists(ElevationCache) || Directory.GetFiles(ElevationCache, "*.tif").Length == 0)
                Skip = "höjdrutorna är inte nedladdade — se tools/arenabild/README.md";
        }
    }

    /// <summary>Ett facittest som behöver hela den cachade scenen: höjdrutor och ortofoto.</summary>
    public sealed class SceneFactAttribute : FactAttribute
    {
        public SceneFactAttribute()
        {
            var cache = Path.Combine(RepoRoot, "tools", "arenabild", "cache");
            if (!Directory.Exists(ElevationCache) || Directory.GetFiles(ElevationCache, "*.tif").Length == 0
                || !Directory.Exists(cache) || Directory.GetFiles(cache, "orto_*.img").Length == 0)
                Skip = "terrängcachen är inte nedladdad — se tools/arenabild/README.md";
        }
    }
}
