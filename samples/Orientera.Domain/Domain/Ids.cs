using System.Text.Json.Serialization;

namespace Orientera.Domain;

/// <summary>
/// An id that is a string underneath. Lets one converter give every id type the same wire
/// form — <c>"12345"</c> rather than <c>{"Value":"12345"}</c> — across the BFF contract and
/// the offline packages.
/// </summary>
public interface IStringId<out TSelf>
{
    string Value { get; }

    static abstract TSelf From(string value);
}

[JsonConverter(typeof(StringIdJsonConverter<CompetitionId>))]
public readonly record struct CompetitionId(string Value) : IStringId<CompetitionId>
{
    public static CompetitionId From(string value) => new(value);

    public override string ToString() => Value;
}

[JsonConverter(typeof(StringIdJsonConverter<EventGroupId>))]
public readonly record struct EventGroupId(string Value) : IStringId<EventGroupId>
{
    public static EventGroupId From(string value) => new(value);

    public override string ToString() => Value;
}

[JsonConverter(typeof(StringIdJsonConverter<PersonId>))]
public readonly record struct PersonId(string Value) : IStringId<PersonId>
{
    public static PersonId From(string value) => new(value);

    public override string ToString() => Value;
}

[JsonConverter(typeof(StringIdJsonConverter<ResultId>))]
public readonly record struct ResultId(string Value) : IStringId<ResultId>
{
    public static ResultId From(string value) => new(value);

    public override string ToString() => Value;
}

[JsonConverter(typeof(StringIdJsonConverter<SeriesId>))]
public readonly record struct SeriesId(string Value) : IStringId<SeriesId>
{
    public static SeriesId From(string value) => new(value);

    public override string ToString() => Value;
}
