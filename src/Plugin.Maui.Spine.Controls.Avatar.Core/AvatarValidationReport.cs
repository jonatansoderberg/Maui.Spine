using System.Text;
using System.Text.Json;

namespace Plugin.Maui.Spine.Controls.Avatar;

/// <summary>The result of one check.</summary>
public enum AvatarCheckStatus
{
    /// <summary>The check passed.</summary>
    Pass,

    /// <summary>Loads, but something is off or ignored.</summary>
    Warning,

    /// <summary>The package does not load.</summary>
    Fail,

    /// <summary>Not checked here (it needs a device or a renderer this runtime lacks).</summary>
    NotMeasured,
}

/// <summary>One check of loading or validation.</summary>
/// <param name="Area">container, manifest, representation id, or runtime.</param>
/// <param name="Name">What was checked (schema, poses, sha256…).</param>
/// <param name="Status">The result.</param>
/// <param name="Detail">What was found, with the file and the JSON path where there is one.</param>
public sealed record AvatarCheck(string Area, string Name, AvatarCheckStatus Status, string? Detail = null);

/// <summary>What loading and validation found: every check with its result, never a bare yes or no.</summary>
public sealed class AvatarValidationReport
{
    private readonly List<AvatarCheck> _checks = [];

    /// <summary>Every check in the order it ran.</summary>
    public IReadOnlyList<AvatarCheck> Checks => _checks;

    /// <summary>Whether any check failed, so the package does not load.</summary>
    public bool HasFailures => _checks.Exists(c => c.Status == AvatarCheckStatus.Fail);

    /// <summary>The failed checks.</summary>
    public IEnumerable<AvatarCheck> Failures => _checks.Where(c => c.Status == AvatarCheckStatus.Fail);

    /// <summary>Records a passed check.</summary>
    public void Pass(string area, string name, string? detail = null) => _checks.Add(new(area, name, AvatarCheckStatus.Pass, detail));

    /// <summary>Records a warning.</summary>
    public void Warn(string area, string name, string detail) => _checks.Add(new(area, name, AvatarCheckStatus.Warning, detail));

    /// <summary>Records a failure.</summary>
    public void Fail(string area, string name, string detail) => _checks.Add(new(area, name, AvatarCheckStatus.Fail, detail));

    /// <summary>Records a check that could not run here.</summary>
    public void NotMeasured(string area, string name, string? detail = null) => _checks.Add(new(area, name, AvatarCheckStatus.NotMeasured, detail));

    /// <summary>Runs <paramref name="check"/>; a format error becomes a failed check with its path and message.</summary>
    public bool Try(string area, string name, Action check)
    {
        try
        {
            check();
            return true;
        }
        catch (AvatarFormatException e)
        {
            Fail(area, name, e.Message);
            return false;
        }
    }

    /// <summary>Counts by status, as <c>Pass 9  Warning 1</c>.</summary>
    public string Summary()
    {
        var text = new StringBuilder();
        foreach (var group in _checks.GroupBy(c => c.Status))
            text.Append(group.Key).Append(' ').Append(group.Count()).Append("  ");
        return text.ToString().TrimEnd();
    }

    /// <summary>The checks as a JSON array, for a tool or a CI log.</summary>
    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartArray();
            foreach (var check in _checks)
            {
                json.WriteStartObject();
                json.WriteString("area", check.Area);
                json.WriteString("name", check.Name);
                json.WriteString("status", check.Status.ToString());
                if (check.Detail is not null)
                    json.WriteString("detail", check.Detail);
                json.WriteEndObject();
            }
            json.WriteEndArray();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
