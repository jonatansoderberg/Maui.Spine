using System.Text.Json;
using System.Text.Json.Serialization;
using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Services;

/// <summary>
/// What Spine keeps of an indexed item. The platform's index hands back only the id when a result is
/// tapped, so the target is kept here: the page by its full type name and the parameter as JSON.
/// </summary>
internal sealed record SearchRecord(
    string Id,
    string Title,
    string? Description,
    string? Icon,
    string[] Keywords,
    string Page,
    string? ParameterType,
    string? Parameter,
    DateTimeOffset Updated)
{
    /// <summary>Whether this is the entry of a <see cref="SearchableAttribute"/> page.</summary>
    public bool IsPage => Id.StartsWith(SearchRecords.PagePrefix, StringComparison.Ordinal);

    /// <summary>Whether <paramref name="other"/> shows and opens the same thing, whenever it was written.</summary>
    public bool SameContent(SearchRecord other) => this with { Updated = other.Updated, Keywords = other.Keywords } == other
        && Keywords.SequenceEqual(other.Keywords);
}

/// <summary>
/// Turns a <see cref="SearchableItem"/> into a <see cref="SearchRecord"/> and back. No MAUI types, so
/// the test project compiles it on plain <c>net10.0</c>.
/// </summary>
internal static class SearchRecords
{
    /// <summary>The id prefix of <see cref="SearchableAttribute"/> pages' entries.</summary>
    public const string PagePrefix = "spine.page:";

    /// <summary>The id of a <see cref="SearchableAttribute"/> page's entry.</summary>
    public static string PageId(Type page) => PagePrefix + page.FullName;

    /// <summary>
    /// The record of <paramref name="item"/>. The parameter is written and read back here, so a type
    /// that cannot make the round trip fails when it is indexed, not when the result is tapped.
    /// </summary>
    /// <exception cref="ArgumentException">The id or title is empty.</exception>
    /// <exception cref="NotSupportedException">The parameter cannot be stored as JSON.</exception>
    public static SearchRecord From(SearchableItem item, JsonSerializerOptions json, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(item.Id)) throw new ArgumentException("A searchable item needs an id.", nameof(item));
        if (string.IsNullOrWhiteSpace(item.Title)) throw new ArgumentException($"Searchable item \"{item.Id}\" needs a title.", nameof(item));

        var target = item.Target;
        string? parameter = null;
        if (target.ParameterType is { } type)
        {
            try
            {
                parameter = JsonSerializer.Serialize(target.Parameter, type, json);
                JsonSerializer.Deserialize(parameter, type, json);
            }
            catch (Exception e) when (e is JsonException or NotSupportedException or InvalidOperationException)
            {
                throw new NotSupportedException(
                    $"Searchable item \"{item.Id}\": the parameter of {target.Page.Name}, a {type.FullName}, cannot be stored as JSON ({e.Message}). " +
                    "Pass something serialisable, such as an id, or give SpineOptions.Search.JsonOptions a resolver for it.", e);
            }
        }

        return new SearchRecord(
            item.Id,
            item.Title,
            item.Description,
            item.Icon,
            [.. item.Keywords ?? []],
            target.Page.FullName!,
            target.ParameterType?.FullName,
            parameter,
            now);
    }

    /// <summary>
    /// The target of <paramref name="record"/>, or <see langword="null"/> with the reason in
    /// <paramref name="problem"/>: the page is gone, it no longer takes that parameter, or the stored
    /// JSON no longer fits the type. <paramref name="findPage"/> gives the registered navigable page with
    /// a full type name, or <see langword="null"/>.
    /// </summary>
    public static NavigationTarget? Resolve(SearchRecord record, Func<string, Type?> findPage, JsonSerializerOptions json, out string? problem)
    {
        problem = null;
        if (findPage(record.Page) is not { } page)
        {
            problem = $"the page {record.Page} no longer exists";
            return null;
        }

        if (record.ParameterType is null)
            return NavigationTarget.Restored(page, null, null);

        var parameterType = page.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(INavigableWithParameter<>))
            .Select(i => i.GetGenericArguments()[0])
            .FirstOrDefault(t => t.FullName == record.ParameterType);

        if (parameterType is null)
        {
            problem = $"{page.Name} no longer takes a {record.ParameterType}";
            return null;
        }

        try
        {
            var parameter = JsonSerializer.Deserialize(record.Parameter ?? "null", parameterType, json);
            return NavigationTarget.Restored(page, parameterType, parameter);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            problem = $"the stored {parameterType.Name} no longer reads as one ({e.Message})";
            return null;
        }
    }

    /// <summary>The records in <paramref name="stream"/>, by id.</summary>
    public static Dictionary<string, SearchRecord> Read(Stream stream) =>
        (JsonSerializer.Deserialize(stream, SearchRecordsJson.Default.ListSearchRecord) ?? [])
            .ToDictionary(r => r.Id, StringComparer.Ordinal);

    /// <summary>Writes <paramref name="records"/> to <paramref name="stream"/>.</summary>
    public static void Write(Stream stream, IEnumerable<SearchRecord> records) =>
        JsonSerializer.Serialize(stream, records.ToList(), SearchRecordsJson.Default.ListSearchRecord);
}

[JsonSerializable(typeof(List<SearchRecord>))]
internal sealed partial class SearchRecordsJson : JsonSerializerContext;
