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
    /// Runs a load, catching only an unreachable source. Anything else is a real defect and
    /// must keep crashing loudly rather than hiding behind an offline message.
    /// </summary>
    protected async Task<bool> LoadAsync(Func<Task> load)
    {
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
    }
}
