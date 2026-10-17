using Plugin.Maui.Spine.Controls.Avatar;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// A short performance for an avatar: what it says, how it looks, what it looks at and when it waits for the user. Played
/// by <see cref="AvatarView.PlayAsync"/> or <see cref="AvatarPresenter.PlayAsync"/>, one step after another.
/// </summary>
/// <example>
/// <code>
/// var script = AvatarScript.Create()
///     .State(AvatarState.Connecting)
///     .Say("Hi, I am Nova.")
///     .Express(AvatarExpression.Happy)
///     .Gesture("nod")
///     .LookAt(addButton).Say("Tap here to add one.")
///     .WaitForTap();
/// await avatar.PlayAsync(script, cancellationToken);
/// </code>
/// </example>
public sealed class AvatarScript
{
    private readonly List<AvatarScriptStep> _steps = [];

    private AvatarScript() { }

    /// <summary>An empty script.</summary>
    public static AvatarScript Create() => new();

    internal IReadOnlyList<AvatarScriptStep> Steps => _steps;

    /// <summary>Sets the avatar's state.</summary>
    public AvatarScript State(AvatarState state) => Add(new AvatarScriptStep.SetState(state));

    /// <summary>
    /// Says <paramref name="text"/>: read aloud when <paramref name="speak"/> and the app has Spine.Speech, otherwise mouthed
    /// from its letters; the next step waits until it is done.
    /// </summary>
    public AvatarScript Say(string text, bool speak = true) => Add(new AvatarScriptStep.Say(text, speak));

    /// <summary>Shows an expression for <paramref name="duration"/> (three seconds by default), then the base expression.</summary>
    public AvatarScript Express(AvatarExpression expression, float intensity = 0.8f, TimeSpan? duration = null) =>
        Add(new AvatarScriptStep.Express(new(expression, intensity, duration ?? TimeSpan.FromSeconds(3))));

    /// <summary>Plays a gesture (nod, shake, lean…) and waits for it to end; nothing when the avatar has no such gesture.</summary>
    public AvatarScript Gesture(string name) => Add(new AvatarScriptStep.Gesture(name));

    /// <summary>Looks at <paramref name="target"/>, wherever it is on screen.</summary>
    public AvatarScript LookAt(VisualElement target) => Add(new AvatarScriptStep.LookAtView(target));

    /// <summary>Looks at a point in window coordinates (a tour's target frame, say).</summary>
    public AvatarScript LookAt(Point windowPoint) => Add(new AvatarScriptStep.LookAtPoint(windowPoint));

    /// <summary>Looks back at the viewer.</summary>
    public AvatarScript LookBack() => Add(new AvatarScriptStep.LookBack());

    /// <summary>Waits.</summary>
    public AvatarScript Wait(TimeSpan delay) => Add(new AvatarScriptStep.Wait(delay));

    /// <summary>Waits until the user taps the avatar (or the presenter), or <see cref="ScriptAvatarDriver.Continue"/> is called.</summary>
    public AvatarScript WaitForTap() => Add(new AvatarScriptStep.WaitForTap());

    private AvatarScript Add(AvatarScriptStep step)
    {
        _steps.Add(step);
        return this;
    }
}

internal abstract record AvatarScriptStep
{
    public sealed record SetState(AvatarState State) : AvatarScriptStep;

    public sealed record Say(string Text, bool Speak) : AvatarScriptStep;

    public sealed record Express(AvatarExpressionRequest Request) : AvatarScriptStep;

    public sealed record Gesture(string Name) : AvatarScriptStep;

    public sealed record LookAtView(VisualElement Target) : AvatarScriptStep;

    public sealed record LookAtPoint(Point WindowPoint) : AvatarScriptStep;

    public sealed record LookBack : AvatarScriptStep;

    public sealed record Wait(TimeSpan Delay) : AvatarScriptStep;

    public sealed record WaitForTap : AvatarScriptStep;
}
