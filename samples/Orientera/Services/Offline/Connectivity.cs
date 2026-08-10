namespace Orientera.Services.Offline;

/// <summary>Raised when a data source cannot be reached. The fallback path catches this.</summary>
public sealed class SourceUnavailableException(string message) : Exception(message);

/// <summary>
/// Whether the app can currently reach its sources. In M0/M1 it is a dev switch, so the
/// offline and error paths can be demonstrated and tested without unplugging anything;
/// real connectivity replaces the flag when a live source exists.
/// </summary>
public sealed class ConnectivitySwitch
{
    private bool _isOffline;

    public event EventHandler? Changed;

    public bool IsOffline
    {
        get => _isOffline;
        set
        {
            if (_isOffline == value)
                return;

            _isOffline = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Toggle() => IsOffline = !IsOffline;
}
