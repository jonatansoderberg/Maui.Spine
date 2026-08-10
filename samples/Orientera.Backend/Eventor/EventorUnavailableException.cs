namespace Orientera.Backend.Eventor;

/// <summary>
/// Eventor could not be reached, or answered with something that is not a result. The BFF
/// turns this into a 502 so the app can tell "the source is down" apart from "there is
/// nothing here", which is the difference between the offline fallback and an empty list.
/// </summary>
public sealed class EventorUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);
