namespace Orientera.Tests;

/// <summary>
/// Two systems, no shared id, and a runner who has to recognise themselves in the list. These
/// pin where the app is willing to say "that is you" and where it is not.
/// </summary>
public class RunnerIdentityTests
{
    [Theory]
    [InlineData("Elin Nordqvist", "Elin  Nordqvist")]
    [InlineData("Elin Nordqvist", "ELIN NORDQVIST")]
    [InlineData("Elin Nordqvist", "Nordqvist, Elin")]
    [InlineData("Jennie Börjesson Eriksson", "jennie börjesson eriksson")]
    [InlineData("Per-Olof Ek", "Per Olof Ek")]
    public void The_same_runner_written_differently_is_the_same_runner(string left, string right) =>
        Assert.True(RunnerIdentity.Of(left).Matches(RunnerIdentity.Of(right)));

    [Fact]
    public void Different_runners_stay_different() =>
        Assert.False(RunnerIdentity.Of("Elin Nordqvist").Matches(RunnerIdentity.Of("Elin Nordquist")));

    /// <summary>
    /// Clubs are written differently by every source, so an unknown or differing club must not
    /// break a match on its own.
    /// </summary>
    [Fact]
    public void A_missing_club_does_not_break_a_match() =>
        Assert.True(RunnerIdentity.Of("Elin Nordqvist", "Gävle OK").Matches(RunnerIdentity.Of("Elin Nordqvist")));

    /// <summary>But two runners who share a name are two runners.</summary>
    [Fact]
    public void A_namesake_in_another_club_is_someone_else() =>
        Assert.False(RunnerIdentity.Of("Anna Berg", "Gävle OK").Matches(RunnerIdentity.Of("Anna Berg", "Sandvikens OK")));

    [Fact]
    public void An_empty_name_matches_nobody() =>
        Assert.False(RunnerIdentity.Of("").Matches(RunnerIdentity.Of("")));

    [Fact]
    public void The_key_is_stable_across_spellings() =>
        Assert.Equal(
            RunnerIdentity.Of("Nordqvist, Elin", "Gävle OK").Key,
            RunnerIdentity.Of("Elin Nordqvist", "GÄVLE OK").Key);
}
