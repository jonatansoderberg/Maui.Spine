using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Extensions;

namespace Plugin.Maui.Spine.Presentation;

/// <summary>One choice in a <see cref="SegmentedControl"/>: a title, an SVG icon, or both.</summary>
public class Segment : Element
{
    /// <summary>Identifies the <see cref="Title"/> bindable property.</summary>
    public static readonly BindableProperty TitleProperty = BindableProperty.Create(
        nameof(Title), typeof(string), typeof(Segment), null);

    /// <summary>Identifies the <see cref="Svg"/> bindable property.</summary>
    public static readonly BindableProperty SvgProperty = BindableProperty.Create(
        nameof(Svg), typeof(string), typeof(Segment), null);

    /// <summary>Identifies the <see cref="IsEnabled"/> bindable property.</summary>
    public static readonly BindableProperty IsEnabledProperty = BindableProperty.Create(
        nameof(IsEnabled), typeof(bool), typeof(Segment), true);

    /// <summary>The segment's text. Also what a screen reader says for an icon-only segment.</summary>
    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>An SVG resource name, e.g. <c>"person.svg"</c>, tinted like the title.</summary>
    public string? Svg
    {
        get => (string?)GetValue(SvgProperty);
        set => SetValue(SvgProperty, value);
    }

    /// <summary>Whether the segment can be picked.</summary>
    public bool IsEnabled
    {
        get => (bool)GetValue(IsEnabledProperty);
        set => SetValue(IsEnabledProperty, value);
    }
}

/// <summary>Raised by a <see cref="SegmentedControl"/> when another segment is picked.</summary>
/// <param name="oldIndex">The segment picked before, or -1.</param>
/// <param name="newIndex">The segment picked now, or -1.</param>
public sealed class SegmentSelectedEventArgs(int oldIndex, int newIndex) : EventArgs
{
    /// <summary>The segment picked before, or -1.</summary>
    public int OldIndex { get; } = oldIndex;

    /// <summary>The segment picked now, or -1.</summary>
    public int NewIndex { get; } = newIndex;
}

/// <summary>
/// A row of mutually exclusive choices, drawn by the platform: <c>UISegmentedControl</c> on iOS
/// and Mac Catalyst (glass on iOS 26), Material segmented buttons on Android and a
/// <c>SelectorBar</c> on Windows.
/// </summary>
/// <remarks>
/// The segments are logical children, so a <see cref="Segment"/> binds against the control's
/// binding context. Changing a segment, or the list, updates the native control in place.
/// <para>
/// <see cref="SelectedSegmentColor"/> unset, the selected segment is the platform's own on Apple
/// (UIKit's neutral thumb) and the app's accent (<see cref="IThemeService.Accent"/>) on Android;
/// Windows draws its selection pill in the system accent.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// &lt;SegmentedControl SelectedIndex="{Binding Range}"&gt;
///     &lt;Segment Title="Day" /&gt;
///     &lt;Segment Title="Week" /&gt;
///     &lt;Segment Title="Month" Svg="calendar.svg" /&gt;
/// &lt;/SegmentedControl&gt;
/// </code>
/// </example>
[ContentProperty(nameof(Segments))]
public partial class SegmentedControl : View
{
    /// <summary>Identifies the <see cref="SelectedIndex"/> bindable property.</summary>
    public static readonly BindableProperty SelectedIndexProperty = BindableProperty.Create(
        nameof(SelectedIndex), typeof(int), typeof(SegmentedControl), 0, BindingMode.TwoWay,
        coerceValue: static (_, value) => Math.Max(-1, (int)value),
        propertyChanged: static (bindable, oldValue, newValue) =>
            ((SegmentedControl)bindable).SelectionChanged?.Invoke(bindable, new SegmentSelectedEventArgs((int)oldValue, (int)newValue)));

    /// <summary>Identifies the <see cref="SelectedSegmentColor"/> bindable property.</summary>
    public static readonly BindableProperty SelectedSegmentColorProperty = BindableProperty.Create(
        nameof(SelectedSegmentColor), typeof(Color), typeof(SegmentedControl), null);

    private readonly ObservableCollection<Segment> _segments = [];
    private readonly List<Segment> _attached = [];

    /// <summary>A control with no segments yet.</summary>
    public SegmentedControl()
    {
        _segments.CollectionChanged += OnSegmentsChanged;
        SpineTheme.Track(this, () => Handler?.UpdateValue(nameof(SelectedSegmentColor)));
    }

    /// <summary>The choices, in order.</summary>
    public IList<Segment> Segments => _segments;

    /// <summary>The index of the picked segment; -1 for none. Two-way.</summary>
    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    /// <summary>
    /// The fill of the picked segment, its text black or white to read on it. Unset, the platform's
    /// own on Apple and the app's accent on Android.
    /// </summary>
    public Color? SelectedSegmentColor
    {
        get => (Color?)GetValue(SelectedSegmentColorProperty);
        set => SetValue(SelectedSegmentColorProperty, value);
    }

    /// <summary>Raised when <see cref="SelectedIndex"/> changes, by a tap or from code.</summary>
    public event EventHandler<SegmentSelectedEventArgs>? SelectionChanged;

    internal static AppTheme CurrentTheme => Application.Current?.RequestedTheme == AppTheme.Dark ? AppTheme.Dark : AppTheme.Light;

    /// <summary>Called by the platform view when the user picks a segment.</summary>
    internal void OnUserSelected(int index)
    {
        if (index == SelectedIndex)
            return;

        SelectedIndex = index;
        Haptics.Play(Haptics.Options.Haptics.TabSwitch);
    }

    private void OnSegmentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var segment in _attached.Except(_segments).ToList())
        {
            segment.PropertyChanged -= OnSegmentPropertyChanged;
            RemoveLogicalChild(segment);
            _attached.Remove(segment);
        }

        foreach (var segment in _segments.Except(_attached).ToList())
        {
            AddLogicalChild(segment);
            segment.PropertyChanged += OnSegmentPropertyChanged;
            _attached.Add(segment);
        }

        Handler?.UpdateValue(nameof(Segments));
    }

    private void OnSegmentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Segment.Title) or nameof(Segment.Svg) or nameof(Segment.IsEnabled))
            Handler?.UpdateValue(nameof(Segments));
    }
}
