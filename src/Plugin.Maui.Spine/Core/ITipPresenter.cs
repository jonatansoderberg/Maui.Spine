namespace Plugin.Maui.Spine.Core;

/// <summary>
/// A view that presents a tip pointing at something on screen: a guide character in a tour card, say. A tour gives it each
/// step as the step opens and tells it when the tour ends; the view shows, mouths or speaks the tip as it likes.
/// </summary>
/// <remarks>Spine's tours (<c>Plugin.Maui.Spine.Controls.Highlight</c>) call it; an avatar's presenter implements it.</remarks>
public interface ITipPresenter
{
    /// <summary>A step opened: present its tip, and look at <see cref="TipPresentation.TargetFrame"/>.</summary>
    void Present(TipPresentation tip);

    /// <summary>The tour or hint ended: stop presenting.</summary>
    void Dismiss();
}

/// <summary>One step a <see cref="ITipPresenter"/> presents.</summary>
/// <param name="Tip">What to say.</param>
/// <param name="Title">A heading, if any.</param>
/// <param name="TargetFrame">The spotlighted element, in window coordinates.</param>
/// <param name="Gesture">A gesture to play as the step opens (nod, lean), if any.</param>
/// <param name="Number">The step's number, from 1.</param>
/// <param name="Count">How many steps the tour has.</param>
public sealed record TipPresentation(string Tip, string? Title, Rect TargetFrame, string? Gesture, int Number, int Count);
