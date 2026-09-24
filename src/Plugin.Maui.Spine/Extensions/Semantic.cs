using System.ComponentModel;

namespace Plugin.Maui.Spine.Extensions;

/// <summary>
/// Makes a layout one element to a screen reader: with <see cref="MergeProperty"/> its children
/// leave the accessibility tree and their texts join into the layout's description, so a row of an
/// icon, a title, a detail and a value is one swipe instead of four.
/// </summary>
/// <remarks>
/// <para>
/// The description is each visible child's own <c>SemanticProperties.Description</c> when it has
/// one, else a <see cref="Label"/>'s text, in layout order, joined with commas. It follows text
/// changes and added or removed children. A description set on the layout itself wins.
/// </para>
/// <para>
/// A <see cref="Switch"/> or <see cref="CheckBox"/> inside makes the merged element a toggle that
/// reports its state (VoiceOver's toggle button, TalkBack's switch); give the layout a
/// <see cref="Tap.CommandProperty"/> that flips it so activating the element toggles it. With
/// <see cref="Tap.CommandProperty"/> the element reads as a button. Anything else that is tappable
/// on its own belongs outside a merged layout: its children are not reachable one by one.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// &lt;Grid ColumnDefinitions="*,Auto" Semantic.Merge="True"&gt;
///     &lt;Label Text="Battery" /&gt;
///     &lt;Label Grid.Column="1" Text="82 %" /&gt;
/// &lt;/Grid&gt;
/// </code>
/// </example>
public static partial class Semantic
{
    /// <summary>Attached property: whether the view's children read as one element.</summary>
    public static readonly BindableProperty MergeProperty =
        BindableProperty.CreateAttached(
            "Merge",
            typeof(bool),
            typeof(Semantic),
            false,
            propertyChanged: OnMergeChanged);

    static readonly BindableProperty StateProperty =
        BindableProperty.CreateAttached("State", typeof(MergeState), typeof(Semantic), null);

    static readonly BindableProperty HiddenByMergeProperty =
        BindableProperty.CreateAttached("HiddenByMerge", typeof(bool), typeof(Semantic), false);

    /// <summary>Gets whether the children of <paramref name="view"/> read as one element.</summary>
    public static bool GetMerge(BindableObject view) => (bool)view.GetValue(MergeProperty);

    /// <summary>Sets whether the children of <paramref name="view"/> read as one element.</summary>
    public static void SetMerge(BindableObject view, bool value) => view.SetValue(MergeProperty, value);

    static void OnMergeChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not View view)
            return;

        var state = (MergeState?)view.GetValue(StateProperty);
        state?.Dispose();
        view.ClearValue(StateProperty);

        if ((bool)newValue)
            view.SetValue(StateProperty, new MergeState(view));
        else
            Refresh(view);
    }

    /// <summary>Re-applies the screen-reader traits of <paramref name="view"/> (button, toggle).</summary>
    internal static void Refresh(View view)
    {
        if (view.GetValue(StateProperty) is MergeState state)
            state.Update();
        else
            ApplyPlatform(view, merged: false, button: Tap.GetCommand(view) is not null, toggled: null, IsEnabled(view));
    }

    static (string Text, bool? Toggled) Collect(View view)
    {
        var parts = new List<string>();
        bool? toggled = null;

        foreach (var child in ((IVisualTreeElement)view).GetVisualChildren())
            Walk(child, parts, ref toggled);

        return (string.Join(", ", parts), toggled);
    }

    static void Walk(IVisualTreeElement element, List<string> parts, ref bool? toggled)
    {
        if (element is VisualElement { IsVisible: false })
            return;

        if (element is BindableObject bindable
            && SemanticProperties.GetDescription(bindable) is { Length: > 0 } description)
        {
            parts.Add(description);
            return;
        }

        switch (element)
        {
            case Label label:
                var text = label.FormattedText?.ToString() is { Length: > 0 } formatted ? formatted : label.Text;
                if (!string.IsNullOrWhiteSpace(text))
                    parts.Add(text.Trim());
                return;
            case Switch toggle:
                toggled ??= toggle.IsToggled;
                return;
            case CheckBox check:
                toggled ??= check.IsChecked;
                return;
        }

        foreach (var child in element.GetVisualChildren())
            Walk(child, parts, ref toggled);
    }

    // A tap target whose command cannot run reads as dimmed, like a disabled button.
    static bool IsEnabled(View view) => view.IsEnabled && (Tap.GetState(view)?.CanExecute ?? true);

    static partial void ApplyPlatform(View view, bool merged, bool button, bool? toggled, bool enabled);

    /// <summary>
    /// Follows a merged view: hides each descendant from the accessibility tree, listens for the
    /// changes that alter the merged text, and writes the text as the view's description.
    /// </summary>
    sealed class MergeState : IDisposable
    {
        static readonly HashSet<string> WatchedProperties =
        [
            Label.TextProperty.PropertyName,
            Label.FormattedTextProperty.PropertyName,
            VisualElement.IsVisibleProperty.PropertyName,
            Switch.IsToggledProperty.PropertyName,
            CheckBox.IsCheckedProperty.PropertyName,
            SemanticProperties.DescriptionProperty.PropertyName,
        ];

        readonly View _view;
        readonly List<Element> _tracked = [];
        string? _written;

        public MergeState(View view)
        {
            _view = view;
            view.DescendantAdded += OnDescendantAdded;
            view.DescendantRemoved += OnDescendantRemoved;
            view.HandlerChanged += OnHandlerChanged;
            view.PropertyChanged += OnViewPropertyChanged;

            foreach (var child in ((IVisualTreeElement)view).GetVisualChildren())
                TrackTree(child);

            Update();
        }

        public void Update()
        {
            var (text, toggled) = Collect(_view);

            var current = SemanticProperties.GetDescription(_view);
            var ownedByApp = _view.IsSet(SemanticProperties.DescriptionProperty) && current != _written;
            if (!ownedByApp && current != text)
            {
                _written = text;
                SemanticProperties.SetDescription(_view, text);
            }

            ApplyPlatform(_view, merged: true, button: Tap.GetCommand(_view) is not null, toggled, IsEnabled(_view));
        }

        public void Dispose()
        {
            _view.DescendantAdded -= OnDescendantAdded;
            _view.DescendantRemoved -= OnDescendantRemoved;
            _view.HandlerChanged -= OnHandlerChanged;
            _view.PropertyChanged -= OnViewPropertyChanged;

            foreach (var element in _tracked)
                Release(element);
            _tracked.Clear();

            if (_written is not null && SemanticProperties.GetDescription(_view) == _written)
                _view.ClearValue(SemanticProperties.DescriptionProperty);
        }

        void TrackTree(IVisualTreeElement element)
        {
            if (element is Element e)
                Track(e);

            foreach (var child in element.GetVisualChildren())
                TrackTree(child);
        }

        void Track(Element element)
        {
            if (_tracked.Contains(element))
                return;

            _tracked.Add(element);
            element.PropertyChanged += OnChildPropertyChanged;

            if (!element.IsSet(AutomationProperties.IsInAccessibleTreeProperty))
            {
                element.SetValue(HiddenByMergeProperty, true);
                AutomationProperties.SetIsInAccessibleTree(element, false);
            }
        }

        void Release(Element element)
        {
            element.PropertyChanged -= OnChildPropertyChanged;

            if ((bool)element.GetValue(HiddenByMergeProperty))
            {
                element.ClearValue(AutomationProperties.IsInAccessibleTreeProperty);
                element.ClearValue(HiddenByMergeProperty);
            }
        }

        void OnDescendantAdded(object? sender, ElementEventArgs e)
        {
            Track(e.Element);
            Update();
        }

        void OnDescendantRemoved(object? sender, ElementEventArgs e)
        {
            if (_tracked.Remove(e.Element))
                Release(e.Element);
            Update();
        }

        void OnHandlerChanged(object? sender, EventArgs e)
        {
            if (_view.Handler is not null)
                Update();
        }

        void OnViewPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // An app-set description replacing the merged one, or cleared back to it.
            if (e.PropertyName == SemanticProperties.DescriptionProperty.PropertyName
                && SemanticProperties.GetDescription(_view) != _written)
                Update();
            else if (e.PropertyName == VisualElement.IsEnabledProperty.PropertyName)
                Update();
        }

        void OnChildPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is { } name && WatchedProperties.Contains(name))
                Update();
        }
    }
}
