namespace Plugin.Maui.Spine.Extensions;

/// <summary>
/// Links a view on one page to a view on the page navigated to, so that a push or a pop between
/// them moves the view from one page to the other. Nothing to register.
/// <list type="bullet">
/// <item>On a view inside the page arriving, it makes a shared element: the view flies from its
/// place on one page to its place on the other while the pages move as usual.</item>
/// <item>On the page arriving itself, it makes a zoom: the page grows out of the view with its tag,
/// shrinks back into it when it leaves, and shrinks under the finger on a back-swipe.</item>
/// </list>
/// </summary>
/// <remarks>
/// Tags pair up within one navigation stack (a region, a tab or a sheet); a page opened as a sheet,
/// a tab switch and a reduced-motion setting play the usual transition. Give each view on a page a
/// tag of its own, such as the item's id: a list matches the row whose tag the page arriving has.
/// A row scrolled out of sight is not a source, and the page moves without it.
/// </remarks>
/// <example>
/// <code>
/// &lt;!-- the list --&gt;
/// &lt;Image Source="{Binding Poster}" Transition.Tag="{Binding Id, StringFormat='poster-{0}'}" /&gt;
///
/// &lt;!-- the detail page --&gt;
/// &lt;Image Source="{Binding Poster}" Transition.Tag="{Binding Id, StringFormat='poster-{0}'}" /&gt;
///
/// &lt;!-- or a page that grows out of the poster --&gt;
/// &lt;SpinePage ... Transition.Tag="{Binding Id, StringFormat='poster-{0}'}"&gt;
/// </code>
/// </example>
public static class Transition
{
    /// <summary>Attached property: the name that pairs this view with a view of the same name on another page.</summary>
    public static readonly BindableProperty TagProperty =
        BindableProperty.CreateAttached(
            "Tag",
            typeof(string),
            typeof(Transition),
            null,
            propertyChanged: OnTagChanged);

    /// <summary>Gets the transition tag of <paramref name="view"/>.</summary>
    public static string? GetTag(BindableObject view) => (string?)view.GetValue(TagProperty);

    /// <summary>Sets the transition tag of <paramref name="view"/>.</summary>
    public static void SetTag(BindableObject view, string? value) => view.SetValue(TagProperty, value);

    // Every view that has had a tag. A list's rows are not reachable from the page through its
    // children on every platform, so the views are found from here and their parents instead.
    private static readonly List<WeakReference<VisualElement>> Tagged = [];

    private static void OnTagChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is not VisualElement view || newValue is null)
            return;

        Tagged.RemoveAll(r => !r.TryGetTarget(out _));
        if (!Tagged.Exists(r => r.TryGetTarget(out var known) && ReferenceEquals(known, view)))
            Tagged.Add(new(view));
    }

    /// <summary>The tagged views inside <paramref name="page"/>, by tag; the page itself included.</summary>
    internal static Dictionary<string, List<VisualElement>> TaggedIn(Element page)
    {
        var found = new Dictionary<string, List<VisualElement>>();

        foreach (var reference in Tagged)
        {
            if (!reference.TryGetTarget(out var view) || GetTag(view) is not { Length: > 0 } tag || !IsInside(view, page))
                continue;

            if (!found.TryGetValue(tag, out var views))
                found[tag] = views = [];

            views.Add(view);
        }

        return found;
    }

    private static bool IsInside(Element view, Element page)
    {
        for (Element? e = view; e is not null; e = e.Parent)
        {
            if (ReferenceEquals(e, page))
                return true;
        }

        return false;
    }
}
