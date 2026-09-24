using System.Windows.Input;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// A swipe action shown on every row. The command parameter is the row item unless
/// <see cref="CommandParameterPath"/> says otherwise; <see cref="IsVisiblePath"/> and
/// <see cref="IsEnabledPath"/> are bool paths on the row item, for actions that apply to some rows only.
/// </summary>
/// <remarks>
/// An <see cref="Element"/> parented to its grid rather than a bare <see cref="BindableObject"/>: a
/// <c>{DynamicResource}</c> resolves by walking an element's parents to the application resources,
/// and a parentless object never finds one.
/// </remarks>
public class DataGridSwipeAction : Element
{
    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(DataGridSwipeAction));

    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(nameof(Text), typeof(string), typeof(DataGridSwipeAction), string.Empty);

    public static readonly BindableProperty BackgroundColorProperty =
        BindableProperty.Create(nameof(BackgroundColor), typeof(Color), typeof(DataGridSwipeAction));

    public static readonly BindableProperty TextColorProperty =
        BindableProperty.Create(nameof(TextColor), typeof(Color), typeof(DataGridSwipeAction));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>An embedded SVG shown above the text, tinted with the text colour (e.g. <c>"Delete.svg"</c>).</summary>
    public string? IconSvg { get; set; }

    /// <summary>An icon-font glyph shown above the text, with <see cref="IconFontFamily"/>.</summary>
    public string? IconGlyph { get; set; }

    public string? IconFontFamily { get; set; }

    public double IconSize { get; set; } = 20;

    /// <summary>The action's fill; null uses <see cref="DataGridStyleOptions.SwipeActionBackgroundColor"/>.</summary>
    public Color? BackgroundColor
    {
        get => (Color?)GetValue(BackgroundColorProperty);
        set => SetValue(BackgroundColorProperty, value);
    }

    /// <summary>Text and icon colour; null uses <see cref="DataGridStyleOptions.SwipeActionTextColor"/>.</summary>
    public Color? TextColor
    {
        get => (Color?)GetValue(TextColorProperty);
        set => SetValue(TextColorProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    /// <summary>A path on the row item used as the command parameter. Defaults to the row item.</summary>
    public string? CommandParameterPath { get; set; }

    /// <summary>A bool path on the row item; the action is hidden on rows where it is false.</summary>
    public string? IsVisiblePath { get; set; }

    /// <summary>A bool path on the row item; the action is disabled on rows where it is false.</summary>
    public string? IsEnabledPath { get; set; }
}
