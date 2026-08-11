using Orientera.Domain;

namespace Orientera.Services.Context;

/// <summary>
/// Which competition the live tab is showing.
/// </summary>
/// <remarks>
/// In memory, not on disk: a live competition is over in a few hours, and remembering it until
/// next week would open the tab on something that finished long ago.
///
/// It is also how "Följ live" on a competition page arrives at the right race. Spine's
/// <c>SwitchToTabAsync</c> carries no parameter — a tab root is not navigated to with an
/// argument — so the page that knows which competition is meant leaves it here, and the tab
/// picks it up when it appears.
/// </remarks>
public sealed class LiveSelection
{
    public CompetitionId? Current { get; private set; }

    public void Select(CompetitionId competition) => Current = competition;

    /// <summary>Forgets a choice that is no longer among the competitions running.</summary>
    public void KeepOnly(IEnumerable<CompetitionId> live)
    {
        if (Current is { } current && !live.Contains(current))
            Current = null;
    }
}
