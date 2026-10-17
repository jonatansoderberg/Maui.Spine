namespace Plugin.Maui.Spine.Controls.Avatar.Core.Tests;

// Tests that count allocations or time frames run alone: another test's garbage collection shows up as a whole
// allocation context (8 KB) on the measuring thread.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class Measurements
{
    public const string Name = "Measurements";

    /// <summary>
    /// The fewest bytes <paramref name="work"/> allocates on this thread in up to three tries. Something allocated every
    /// frame shows in every try; the runtime now and then counts a whole allocation context when a collection lands in
    /// the window (2168 and 8080 B were seen), and that does not repeat.
    /// </summary>
    public static long Allocated(Action work)
    {
        var fewest = long.MaxValue;
        for (var i = 0; i < 3 && fewest > 0; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            work();
            fewest = Math.Min(fewest, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        return fewest;
    }
}
