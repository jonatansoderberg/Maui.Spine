using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Plugin.Maui.Spine.Common;

/// <summary>
/// How the Spine keys travel through WNS. The server writes them and the Windows app reads them back,
/// so both halves go through this one class.
/// </summary>
internal static class WnsPayload
{
    /// <summary>
    /// The key a toast button adds to the toast's arguments, naming the button. A button's arguments are
    /// all the app gets when it is tapped, so they repeat the toast's and add this. Reserved: a data bag
    /// that carried it would make a tap on the toast itself read as a tap on that button.
    /// </summary>
    internal const string Action = "spine.action";

    /// <summary>The longest tag a toast may have: WNS refuses an <c>X-WNS-Tag</c> over 16 characters.</summary>
    internal const int TagLength = 16;

    /// <summary>
    /// The toast tag for a collapse id: the server's <c>X-WNS-Tag</c> and the tag of a toast the app draws
    /// itself, so either replaces an earlier one with the same id. Hashed, since an id may be longer
    /// than <see cref="TagLength"/>.
    /// </summary>
    internal static string Tag(string id) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id)))[..TagLength];

    /// <summary>
    /// Writes <paramref name="data"/> as a toast argument: <c>key=value;key=value</c>, with <c>%</c>,
    /// <c>;</c> and <c>=</c> percent-encoded as <c>AppNotificationBuilder.AddArgument</c> does, so a value
    /// cannot break the split.
    /// </summary>
    internal static string WriteArguments(IEnumerable<KeyValuePair<string, string>> data) =>
        string.Join(';', data.Select(pair => $"{Escape(pair.Key)}={Escape(pair.Value)}"));

    /// <summary>Reads an argument written by <see cref="WriteArguments"/>.</summary>
    internal static Dictionary<string, string> ReadArguments(string? arguments)
    {
        var data = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(arguments)) return data;

        foreach (var pair in arguments.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = pair.IndexOf('=');
            if (split < 0) data[Unescape(pair)] = "";
            else data[Unescape(pair[..split])] = Unescape(pair[(split + 1)..]);
        }

        return data;
    }

    /// <summary>Writes a raw notification's body: the data as one JSON object of strings.</summary>
    internal static string WriteRaw(IEnumerable<KeyValuePair<string, string>> data)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var (key, value) in data) writer.WriteString(key, value);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// Reads a raw notification's body. A value that is not a string — a hand-written payload — is kept
    /// as its JSON text rather than dropped.
    /// </summary>
    /// <exception cref="JsonException">The body is not a JSON object.</exception>
    internal static Dictionary<string, string> ReadRaw(byte[] payload)
    {
        using var document = JsonDocument.Parse(payload);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException($"A WNS raw notification for Spine is a JSON object; this one is {document.RootElement.ValueKind}.");

        var data = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            data[property.Name] = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString()!
                : property.Value.GetRawText();
        }

        return data;
    }

    private static string Escape(string value) => value.Replace("%", "%25").Replace(";", "%3B").Replace("=", "%3D");

    // %25 last: every literal % was written as %25, so no %3B or %3D can be made out of one.
    private static string Unescape(string value) => value.Replace("%3B", ";").Replace("%3D", "=").Replace("%25", "%");
}
