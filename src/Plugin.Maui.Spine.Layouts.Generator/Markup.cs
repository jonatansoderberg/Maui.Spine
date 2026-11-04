using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Plugin.Maui.Spine.Layouts;

/// <summary>An attribute's value: literal text, one markup extension, or text mixed with markup.</summary>
internal abstract class Value
{
    /// <summary>
    /// Parses an attribute. <c>{Name}</c> is a binding, <c>{Loc …}</c>, <c>{Static …}</c> and the rest are extensions, and
    /// <c>{}</c> at the start makes the rest literal, as in XAML.
    /// </summary>
    public static Value Parse(string text)
    {
        if (text.StartsWith("{}", System.StringComparison.Ordinal))
            return new Literal(text.Substring(2));
        if (text.IndexOf('{') < 0)
            return new Literal(text);

        var parts = new List<Value>();
        var literal = new StringBuilder();
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '{')
            {
                if (literal.Length > 0) { parts.Add(new Literal(literal.ToString())); literal.Clear(); }
                parts.Add(Markup.Read(text, ref i));
            }
            else
                literal.Append(text[i++]);
        }
        if (literal.Length > 0) parts.Add(new Literal(literal.ToString()));
        return parts.Count == 1 ? parts[0] : new Mixed(parts);
    }
}

internal sealed class Literal(string text) : Value
{
    public string Text { get; } = text;
}

internal sealed class Mixed(List<Value> parts) : Value
{
    public List<Value> Parts { get; } = parts;
}

/// <summary>
/// <c>{Extension positional, …, Key=Value, …}</c>; a binding has no extension name and its path as the first positional
/// value. A value is a nested markup, a quoted string (<c>Prefix=' · '</c>, spaces kept) or text up to the next comma or
/// brace, trimmed.
/// </summary>
internal sealed class Markup : Value
{
    public static readonly HashSet<string> Extensions =
        ["Binding", "Static", "StaticResource", "Loc", "OnPlatform", "OnSize", "OnBool", "AppThemeBinding", "LinearGradient"];

    public string Name { get; private set; } = "Binding";
    public List<Value> Positional { get; } = [];
    public List<KeyValuePair<string, Value>> Named { get; } = [];
    public string? Error { get; private set; }

    public string? Path => Name == "Binding" && Positional.FirstOrDefault() is Literal path ? path.Text : null;

    public Value? this[string key] => Named.FirstOrDefault(n => n.Key == key).Value;

    public static Markup Read(string text, ref int i)
    {
        var markup = new Markup();
        i++; // {
        Skip(text, ref i);
        var first = Token(text, ref i);
        Skip(text, ref i);
        if (Extensions.Contains(first) && first != "Binding")
            markup.Name = first;
        else if (first == "Binding")
            markup.Name = "Binding";
        else if (first.Length > 0 && !(i < text.Length && text[i] == '='))
            markup.Positional.Add(new Literal(first));
        else if (first.Length > 0)
        {
            // {Key=Value, …} with no path.
            i++; // =
            markup.Named.Add(new KeyValuePair<string, Value>(first, ReadItem(text, ref i, markup)));
            Skip(text, ref i);
            if (i < text.Length && text[i] == ',') i++;
        }

        while (i < text.Length && text[i] != '}')
        {
            Skip(text, ref i);
            if (i < text.Length && text[i] == ',') { i++; continue; }
            if (i < text.Length && text[i] == '}') break;
            var start = i;
            var key = Token(text, ref i);
            Skip(text, ref i);
            if (key.Length > 0 && i < text.Length && text[i] == '=')
            {
                i++;
                markup.Named.Add(new KeyValuePair<string, Value>(key, ReadItem(text, ref i, markup)));
            }
            else
            {
                i = start;
                markup.Positional.Add(ReadItem(text, ref i, markup));
            }
            Skip(text, ref i);
        }
        if (i >= text.Length)
            markup.Error ??= "a '}' is missing";
        else
            i++; // }
        return markup;
    }

    static Value ReadItem(string text, ref int i, Markup owner)
    {
        Skip(text, ref i);
        if (i < text.Length && text[i] == '{')
            return Read(text, ref i);
        if (i < text.Length && text[i] == '\'')
        {
            var end = text.IndexOf('\'', i + 1);
            if (end < 0) { owner.Error ??= "a closing quote is missing"; end = text.Length; }
            var quoted = text.Substring(i + 1, end - i - 1);
            i = System.Math.Min(text.Length, end + 1);
            // Quoted text keeps its spaces and may hold bindings of its own: Fallback='v {Week}'.
            return Value.Parse(quoted);
        }
        // Up to the next comma or brace outside a binding of its own: Fallback=v {Week}.
        var builder = new StringBuilder();
        var depth = 0;
        while (i < text.Length && (depth > 0 || text[i] != ',' && text[i] != '}'))
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}') depth--;
            builder.Append(text[i++]);
        }
        return Value.Parse(builder.ToString().Trim());
    }

    static string Token(string text, ref int i)
    {
        var start = i;
        while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '.' || text[i] == '_'))
            i++;
        return text.Substring(start, i - start);
    }

    static void Skip(string text, ref int i)
    {
        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
    }
}
