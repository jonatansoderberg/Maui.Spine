using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// One step of a tour: what to point at and what to say. The target is a <see cref="VisualElement"/>, a
/// <see cref="PageAction"/> (its button in the header bar) or a tab page's <see cref="Type"/> (its item in
/// the tab bar).
/// </summary>
/// <example>
/// <code>
/// await highlights.TourAsync("onboarding",
///     (addButton, "Add an event"),
///     new TourStep(ViewModel.FilterAction, "Filter by calendar") { Title = "Filters" },
///     new TourStep(typeof(SettingsPage), "Your calendars live here"));
/// </code>
/// </example>
public sealed record TourStep(object Target, string Tip)
{
    /// <summary>A short heading above the tip.</summary>
    public string? Title { get; init; }

    /// <summary>The room, in points, between the target and the edge of the lit hole round it.</summary>
    public double Padding { get; init; } = 8;

    /// <summary>
    /// A view shown in the tour card beside the tip, such as an avatar presenter. When it is an <see cref="ITipPresenter"/>
    /// it is given the tip, the target's frame and <see cref="Gesture"/> as the step opens. The same view can present every step.
    /// </summary>
    public View? Presenter { get; init; }

    /// <summary>A gesture the <see cref="Presenter"/> plays as the step opens (nod, lean).</summary>
    public string? Gesture { get; init; }

    public static implicit operator TourStep((VisualElement Target, string Tip) step) => new(step.Target, step.Tip);
}

/// <summary>How a tour ended.</summary>
public enum TourOutcome
{
    /// <summary>The user went through every step.</summary>
    Completed,

    /// <summary>The user skipped it: Skip, Back on Android or Escape on Windows.</summary>
    Skipped,

    /// <summary>The key had been seen, so nothing was shown.</summary>
    AlreadySeen,

    /// <summary>No step could be shown, or the pages on screen changed while it ran. The key is not marked seen.</summary>
    Cancelled,
}

/// <summary>
/// How a tour ended, and the steps it could not show: in <c>Missing</c>, each step left out by its index
/// from zero, with the reason, such as "not on screen".
/// </summary>
public sealed record TourResult(TourOutcome Outcome, IReadOnlyList<(int Step, string Reason)> Missing);
