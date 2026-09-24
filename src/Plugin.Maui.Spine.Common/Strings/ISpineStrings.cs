using System.Globalization;

namespace Plugin.Maui.Spine.Common;

/// <summary>
/// The app's user-facing text: one store that the app and Spine's controls read the same way.
/// </summary>
/// <remarks>
/// Keys are dotted and namespaced: a Spine package owns a prefix (<c>Header.*</c>, <c>Calendar.*</c>)
/// and the app owns the rest. Text comes from an ordered list of <see cref="IStringProvider"/>s,
/// the app's first and each package's defaults after them, so an app overrides any key a control
/// uses by defining it in its own provider. A key missing everywhere resolves to the key itself,
/// so it is visible on screen, and is reported once through <see cref="Missing"/>.
/// </remarks>
public interface ISpineStrings
{
    /// <summary>The text for <paramref name="key"/> in <see cref="Culture"/>, or the key when missing.</summary>
    string this[string key] { get; }

    /// <summary>The text for <paramref name="key"/> formatted with <paramref name="args"/> in <see cref="Culture"/>.</summary>
    string Get(string key, params object?[] args);

    /// <summary>
    /// The plural form for <paramref name="count"/>: <c>key.one</c> for one, <c>key.zero</c> for
    /// none when that key exists, otherwise <c>key.other</c>; the count is <c>{0}</c>.
    /// </summary>
    string Get(string key, int count);

    /// <summary>
    /// The culture text is resolved in. Defaults to <see cref="CultureInfo.CurrentUICulture"/>;
    /// setting it switches at runtime and raises <see cref="Changed"/>.
    /// </summary>
    CultureInfo Culture { get; set; }

    /// <summary>Raised after the culture changed or the providers were reloaded, so bound text re-resolves.</summary>
    event EventHandler? Changed;

    /// <summary>Raised once per key that no provider has, with the key.</summary>
    event EventHandler<string>? Missing;
}

/// <summary>
/// A source of text for one culture at a time. Implement it for anything that is not the built-in
/// XML or <c>.resx</c>: a database, a remote file, a generated class.
/// </summary>
public interface IStringProvider
{
    /// <summary>
    /// The key/value pairs this provider has for exactly <paramref name="culture"/>, or
    /// <see langword="null"/> when it has none. The store walks the culture's parents itself
    /// (<c>sv-SE</c>, then <c>sv</c>, then the invariant culture), so return only the exact match.
    /// Called once per culture; the result is cached until <see cref="SpineStrings.Reload"/>.
    /// </summary>
    IReadOnlyDictionary<string, string>? Load(CultureInfo culture);
}
