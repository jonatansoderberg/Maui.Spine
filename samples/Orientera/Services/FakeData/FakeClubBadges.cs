namespace Orientera.Services.FakeData;

/// <summary>
/// Badges for the invented clubs, so the demo has the same row shape as the real thing.
/// </summary>
/// <remarks>
/// Real badges come from Eventor's organisation register and only the backend sets them. Without
/// an equivalent here every demo row lost its badge: a different row height, the club name
/// starting somewhere else, and the <c>ClubBadge</c> frame never drawn — so design work done
/// against the demo missed all of it (#69).
///
/// This is not the app borrowing from the fake dataset. That rule protects the <em>real</em> path,
/// where an unintegrated answer must be empty rather than invented. The fake dataset is the
/// opposite: a complete, designed fixture, and a club without a badge is simply an unfinished one.
///
/// The badges are plainly invented — flat geometry in colours no Swedish club uses as its mark —
/// rather than imitations of anyone's real one.
/// </remarks>
internal static class FakeClubBadges
{
    private const int Count = 6;

    /// <summary>
    /// The same club gets the same badge on every run, like everything else in the seed.
    /// </summary>
    /// <remarks>
    /// A stable hash, not <see cref="string.GetHashCode()"/>: that one is randomised per process
    /// on .NET Core, which would hand a club a different badge each launch — the one thing a
    /// fixture may not do.
    /// </remarks>
    public static string? For(string? club)
    {
        if (string.IsNullOrWhiteSpace(club))
            return null;

        uint hash = 2166136261;

        foreach (char c in club)
        {
            hash ^= c;
            hash *= 16777619;
        }

        return $"club_badge_{(hash % Count) + 1}.png";
    }
}
