using Orientera.Services.Offline;

namespace Orientera.Presentation;

/// <summary>
/// Base for every page ViewModel that reads a data source.
/// </summary>
/// <remarks>
/// A source that cannot be reached throws, and an unhandled throw inside a lifecycle hook takes
/// the app down. The NFR is the opposite — a missing integration degrades to a designed state,
/// it never blocks the flow — so loading goes through <see cref="LoadAsync"/>, which turns an
/// outage into <see cref="IsOffline"/> for the view to render.
/// </remarks>
public abstract partial class OrienteraViewModel : ViewModelBase
{
    /// <summary>True when the last load could not reach the sources.</summary>
    [ObservableProperty]
    public partial bool IsOffline { get; set; }

    /// <summary>Whether content is available to show — false when offline with nothing cached.</summary>
    [ObservableProperty]
    public partial bool HasContent { get; set; } = true;

    /// <summary>
    /// True while a load is running.
    /// </summary>
    /// <remarks>
    /// Set here rather than per page, because every page's reading goes through
    /// <see cref="LoadAsync"/> and a flag each page had to remember to raise is a flag some page
    /// would forget. Until this existed a slow calendar left the list showing nothing at all —
    /// not the empty state, which is only set once a load has finished, and not a spinner. A
    /// blank screen and a finished-but-empty screen looked identical.
    /// </remarks>
    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>
    /// Drops whatever the page says when it has nothing, for the duration of a load.
    /// </summary>
    /// <remarks>
    /// A page with an empty state overrides this; one without needs nothing. Deliberately not the
    /// same flag as <see cref="HasContent"/> — pages phrase emptiness in their own words and with
    /// their own condition, and unifying them here would flatten "inga resultat ännu" and "inget
    /// att visa" into one sentence that fits neither.
    /// </remarks>
    protected virtual void ClearEmptyState() { }

    /// <summary>
    /// Runs a load, catching only an unreachable source. Anything else is a real defect and
    /// must keep crashing loudly rather than hiding behind an offline message.
    /// </summary>
    protected async Task<bool> LoadAsync(Func<Task> load)
    {
        IsLoading = true;

        // Nothing is empty while the answer is unknown. The flag survives from the previous load,
        // so a page that had no rows last time showed "Inget resultat ännu" on top of the spinner
        // — a wrong statement and a correct one at the same time.
        ClearEmptyState();

        try
        {
            await load();
            IsOffline = false;
            return true;
        }
        catch (SourceUnavailableException)
        {
            IsOffline = true;
            return false;
        }
        finally
        {
            IsLoading = false;
        }
    }
}
