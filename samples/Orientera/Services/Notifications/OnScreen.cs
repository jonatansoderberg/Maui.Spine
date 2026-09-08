using Orientera.Domain;

namespace Orientera.Services.Notifications;

/// <summary>
/// Which competition is on screen right now, so a notification about it does not interrupt the page
/// that is already showing it. Set by the page itself; nothing else knows.
/// </summary>
public sealed class OnScreen
{
    public CompetitionId? Competition { get; private set; }

    public void Show(CompetitionId competition) => Competition = competition;

    /// <summary>Clears it, unless another page has already claimed the screen.</summary>
    public void Hide(CompetitionId competition)
    {
        if (Competition == competition)
            Competition = null;
    }
}
