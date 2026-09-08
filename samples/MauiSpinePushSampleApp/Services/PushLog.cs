using System.Collections.ObjectModel;

namespace MauiSpinePushSampleApp.Services;

/// <summary>One thing that happened, as the Log page shows it.</summary>
/// <param name="At">When.</param>
/// <param name="Kind">What kind of message, or a word for something that was not a message.</param>
/// <param name="Where">Foreground, background or cold start.</param>
/// <param name="Title">The notification's first line, when there was one.</param>
/// <param name="Route">The route the message carried.</param>
/// <param name="Answer">What the handler answered, when it was asked.</param>
/// <param name="Data">Everything the payload carried.</param>
public sealed record PushLogEntry(
    DateTimeOffset At,
    string Kind,
    string Where,
    string? Title,
    string? Route,
    string? Answer,
    IReadOnlyDictionary<string, string> Data)
{
    /// <summary>The one-line summary the list shows.</summary>
    public string Summary => $"{At:HH:mm:ss}  {Kind}  {Where}{(Title is null ? "" : $"  “{Title}”")}";

    /// <summary>The detail line under it.</summary>
    public string Detail =>
        (Route is null ? "" : $"route: {Route}   ") +
        (Answer is null ? "" : $"answered: {Answer}   ") +
        string.Join("  ", Data.Where(p => p.Key.StartsWith("spine.", StringComparison.Ordinal) is false)
                              .Select(p => $"{p.Key}={p.Value}"));
}

/// <summary>
/// Everything the handler has seen, newest first. A singleton so the Log page and the handler share
/// it; in a real app this would be whatever the app already logs to.
/// </summary>
public sealed class PushLog
{
    private const int Limit = 200;

    /// <summary>The entries, newest first. Safe to bind to.</summary>
    public ObservableCollection<PushLogEntry> Entries { get; } = [];

    /// <summary>Adds an entry on the main thread, so a binding can follow it.</summary>
    /// <param name="entry">What happened.</param>
    public void Add(PushLogEntry entry) => MainThread.BeginInvokeOnMainThread(() =>
    {
        Entries.Insert(0, entry);
        while (Entries.Count > Limit) Entries.RemoveAt(Entries.Count - 1);
    });

    /// <summary>Notes something that was not a received message — a registration, an error.</summary>
    /// <param name="what">A word for it.</param>
    /// <param name="detail">What to show beside it.</param>
    public void Note(string what, string detail) =>
        Add(new PushLogEntry(DateTimeOffset.Now, what, detail, null, null, null,
            new Dictionary<string, string>()));

    /// <summary>Forgets everything.</summary>
    public void Clear() => MainThread.BeginInvokeOnMainThread(Entries.Clear);
}
