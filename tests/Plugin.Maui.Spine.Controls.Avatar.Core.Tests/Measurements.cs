namespace Plugin.Maui.Spine.Controls.Avatar.Core.Tests;

// Tests that count allocations or time frames run alone: another test's garbage collection shows up as a whole
// allocation context (8 KB) on the measuring thread.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class Measurements
{
    public const string Name = "Measurements";
}
