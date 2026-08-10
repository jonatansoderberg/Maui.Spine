namespace Orientera.Backend.Upstream;

/// <summary>
/// A source could not be reached, or answered with something that is not a result. The BFF
/// turns this into a 502 so the app can tell "the source is down" apart from "there is nothing
/// here" — the difference between the offline fallback and an empty list.
/// </summary>
public sealed class UpstreamUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);
