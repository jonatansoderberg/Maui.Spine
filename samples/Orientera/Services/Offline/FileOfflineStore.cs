using System.Text.Json;
using System.Text.Json.Serialization;
using Orientera.Domain;

namespace Orientera.Services.Offline;

/// <summary>
/// Stores each competition package as a JSON document under the app's data directory.
/// </summary>
/// <remarks>
/// One file per competition rather than a single index: a package is always read and written
/// whole, and a partial write can then only ever corrupt the one competition it belongs to.
/// </remarks>
public sealed class FileOfflineStore : IOfflineStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _directory;

    public FileOfflineStore(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(_directory);
    }

    public async Task<CompetitionPackage?> GetAsync(
        CompetitionId competition,
        CancellationToken cancellationToken = default)
    {
        var path = PathFor(competition);

        if (!File.Exists(path))
            return null;

        await _gate.WaitAsync(cancellationToken);

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<CompetitionPackage>(stream, Json, cancellationToken);
        }
        catch (Exception)
        {
            // A package that will not deserialise is worse than no package: drop it and let the
            // next refresh rebuild it rather than failing every read from here on.
            TryDelete(path);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<CompetitionPackage>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var packages = new List<CompetitionPackage>();

        foreach (var path in Directory.EnumerateFiles(_directory, "*.json"))
        {
            var id = new CompetitionId(Path.GetFileNameWithoutExtension(path));

            if (await GetAsync(id, cancellationToken) is { } package)
                packages.Add(package);
        }

        return packages;
    }

    public async Task SaveAsync(CompetitionPackage package, CancellationToken cancellationToken = default)
    {
        var path = PathFor(package.Competition.Id);
        var temporary = path + ".tmp";

        await _gate.WaitAsync(cancellationToken);

        try
        {
            // Write then move: a package interrupted mid-write never replaces a good one.
            await using (var stream = File.Create(temporary))
                await JsonSerializer.SerializeAsync(stream, package, Json, cancellationToken);

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            TryDelete(temporary);
            _gate.Release();
        }
    }

    public Task RemoveAsync(CompetitionId competition, CancellationToken cancellationToken = default)
    {
        TryDelete(PathFor(competition));
        return Task.CompletedTask;
    }

    private string PathFor(CompetitionId competition) =>
        Path.Combine(_directory, $"{Sanitize(competition.Value)}.json");

    /// <summary>Ids are our own slugs, but a file name must not trust that.</summary>
    private static string Sanitize(string id)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(id.Select(c => invalid.Contains(c) ? '_' : c));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // Nothing useful to do; the next write overwrites it.
        }
    }
}
