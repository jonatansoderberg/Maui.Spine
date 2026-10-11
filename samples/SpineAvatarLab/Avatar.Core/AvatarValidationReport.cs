using System.Text;
using System.Text.Json;

namespace Plugin.Maui.Spine.Controls.Avatar.Core;

public enum AvatarCheckStatus { Pass, Warning, Fail, NotMeasured }

/// <param name="Area">container, manifest, representation id, or runtime.</param>
public sealed record AvatarCheck(string Area, string Name, AvatarCheckStatus Status, string? Detail = null);

/// <summary>What loading and validation found: every check with its result, never a bare yes or no.</summary>
public sealed class AvatarValidationReport
{
    private readonly List<AvatarCheck> _checks = [];

    public IReadOnlyList<AvatarCheck> Checks => _checks;

    public bool HasFailures => _checks.Exists(c => c.Status == AvatarCheckStatus.Fail);

    public IEnumerable<AvatarCheck> Failures => _checks.Where(c => c.Status == AvatarCheckStatus.Fail);

    public void Pass(string area, string name, string? detail = null) => _checks.Add(new(area, name, AvatarCheckStatus.Pass, detail));

    public void Warn(string area, string name, string detail) => _checks.Add(new(area, name, AvatarCheckStatus.Warning, detail));

    public void Fail(string area, string name, string detail) => _checks.Add(new(area, name, AvatarCheckStatus.Fail, detail));

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

    public string Summary()
    {
        var text = new StringBuilder();
        foreach (var group in _checks.GroupBy(c => c.Status))
            text.Append(group.Key).Append(' ').Append(group.Count()).Append("  ");
        return text.ToString().TrimEnd();
    }

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
