namespace Orientera.Features.Profile;

/// <summary>
/// How the login sheet was opened.
/// </summary>
/// <param name="UseSavedPassword">
/// True when the app is logging the runner back in from what it remembers, rather than asking
/// them to. The sheet is the same page either way — only the typing differs.
/// </param>
public sealed record EventorLoginRequest(bool UseSavedPassword = false);
