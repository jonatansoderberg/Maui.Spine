#if IOS || MACCATALYST

namespace Plugin.Maui.Spine.Extensions;

/// <summary>
/// <see cref="SafeArea.PageMarginProperty"/> for a list on iOS and Mac Catalyst: the page margin on each row's view,
/// and on the list's header, footer and empty view. On iOS 26, MAUI 10.0.60 and later give every section of a list
/// <c>ContentInsetsReference = None</c>, so the layout margins Spine used to inset the sections with no longer
/// reach the rows, and nothing on the collection view itself does. A grid keeps the layout margins
/// (<c>ApplyPageMargin</c>): a margin on every cell would double the gap between its columns.
/// </summary>
static class RowMargins
{
    static readonly BindableProperty FollowingProperty = BindableProperty.CreateAttached(
        "RowMarginsFollowing", typeof(bool), typeof(RowMargins), false);

    /// <summary>What this class added to a row's own margin, so it can be taken off again and never added twice.</summary>
    static readonly BindableProperty AddedProperty = BindableProperty.CreateAttached(
        "RowMarginAdded", typeof(Thickness), typeof(RowMargins), Thickness.Zero);

    /// <summary>Whether <paramref name="view"/>'s rows carry the page margin themselves: a list with it that is not a grid.</summary>
    internal static bool PerRow(View view) =>
        view is ItemsView && SafeArea.GetPageMargin(view) && view is not StructuredItemsView { ItemsLayout: GridItemsLayout };

    internal static void Update(View view)
    {
        if (view is not ItemsView list)
            return;

        if (!(bool)list.GetValue(FollowingProperty))
        {
            list.SetValue(FollowingProperty, true);
            // A row's view is made when the list first needs it and then reused; it joins the list as a child once.
            list.ChildAdded += (_, e) =>
            {
                if (e.Element is View child)
                    Apply(list, child);
            };
            list.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(StructuredItemsView.ItemsLayout))
                    ApplyAll(list);
            };
        }

        ApplyAll(list);
    }

    static void ApplyAll(ItemsView list)
    {
        foreach (var child in ((IVisualTreeElement)list).GetVisualChildren().OfType<View>())
            Apply(list, child);
    }

    static void Apply(ItemsView list, View child)
    {
        var wanted = PerRow(list) ? SafeArea.PageMarginOf(list) : Thickness.Zero;
        var added = (Thickness)child.GetValue(AddedProperty);
        if (wanted == added)
            return;

        var margin = child.Margin;
        child.Margin = new Thickness(margin.Left - added.Left + wanted.Left, margin.Top, margin.Right - added.Right + wanted.Right, margin.Bottom);
        child.SetValue(AddedProperty, wanted);
    }
}

#endif
