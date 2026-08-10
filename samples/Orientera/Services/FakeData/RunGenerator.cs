using Orientera.Domain;

namespace Orientera.Services.FakeData;

/// <summary>Per-runner shaping of a generated race.</summary>
/// <param name="Pace">Multiplier on the reference leg times. 1.00 is winning pace.</param>
/// <param name="Mistakes">Extra seconds on specific control numbers — the deliberate bommar.</param>
/// <param name="Status">Outcome; a mispunch still produces splits, it just does not rank.</param>
public sealed record RunShape(
    double Pace,
    IReadOnlyDictionary<int, int>? Mistakes = null,
    ResultStatus Status = ResultStatus.Ok);

/// <summary>
/// Builds split sequences from a reference course profile. Named runners get an explicit
/// <see cref="RunShape"/> so the narrative holds (Elin loses two minutes on the long legs and
/// lands fifth); everyone else gets a stable pace derived from their id.
/// </summary>
public static class RunGenerator
{
    /// <summary>Reference leg times in seconds — the pace a class winner would run.</summary>
    public static readonly IReadOnlyList<int> LongCourse =
        [245, 380, 165, 520, 290, 410, 195, 640, 310, 225, 480, 270, 350, 110];

    public static readonly IReadOnlyList<int> MiddleCourse =
        [180, 265, 140, 330, 215, 290, 165, 240, 195, 145];

    public static readonly IReadOnlyList<int> SprintCourse =
        [55, 78, 42, 95, 61, 70, 38, 84, 52, 66, 45, 58];

    public static readonly IReadOnlyList<int> YouthCourse =
        [200, 285, 160, 240, 310, 175, 265, 150];

    /// <param name="courseKey">Identifies the course; control codes are stable across its runners.</param>
    /// <param name="runnerKey">Identifies the run; drives the leg-to-leg variation.</param>
    public static IReadOnlyList<Split> Build(
        IReadOnlyList<int> referenceLegs,
        RunShape shape,
        string courseKey,
        string runnerKey)
    {
        var jitter = Deterministic.For(runnerKey);
        var splits = new List<Split>(referenceLegs.Count);
        var elapsed = TimeSpan.Zero;

        for (int i = 0; i < referenceLegs.Count; i++)
        {
            int control = i + 1;

            // ±6% leg-to-leg variation so no two runners are scaled copies of each other.
            double variation = 0.94 + (jitter.NextDouble() * 0.12);
            double seconds = referenceLegs[i] * shape.Pace * variation;

            if (shape.Mistakes?.TryGetValue(control, out int penalty) == true)
                seconds += penalty;

            var legTime = TimeSpan.FromSeconds(Math.Round(seconds));
            elapsed += legTime;

            splits.Add(new Split
            {
                ControlNumber = control,
                ControlCode = ControlCode(control, courseKey),
                LegTime = legTime,
                ElapsedTime = elapsed,
            });
        }

        return splits;
    }

    /// <summary>A stable pace for a runner with no scripted role.</summary>
    public static double PaceFor(PersonId person, CompetitionId competition) =>
        Math.Round(Deterministic.Between(1.02, 1.42, person.Value, competition.Value), 3);

    /// <summary>Codes in the 31–99 range orienteers expect, stable per course and control.</summary>
    private static string ControlCode(int control, string courseKey) =>
        (31 + ((Deterministic.Seed(courseKey, control.ToString()) + control) % 69)).ToString();
}
