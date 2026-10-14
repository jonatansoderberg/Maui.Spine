using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Hosting;
using UIElement = Microsoft.UI.Xaml.UIElement;

namespace Plugin.Maui.Spine.Core;

/// <summary>
/// Composition visuals drawn above an element's content, shared between packages. An element takes only
/// one child visual (<see cref="ElementCompositionPreview.SetElementChildVisual"/>), so a second caller
/// would replace the first; here each visual goes into one container per element, in a fixed order.
/// </summary>
/// <example>
/// <code>
/// ElementVisuals.Add(element, glow, order: 9_000);
/// …
/// ElementVisuals.Remove(element, glow);
/// </code>
/// </example>
public static class ElementVisuals
{
    /// <summary>The order of <c>Tap</c>'s press fill; drawn above lower orders.</summary>
    public const int TapOrder = 10_000;

    static readonly ConditionalWeakTable<UIElement, Container> Containers = [];

    /// <summary>
    /// Draws <paramref name="visual"/> above <paramref name="element"/>'s content, above every visual added
    /// with a lower <paramref name="order"/> and below every one with a higher order. The container is the
    /// element's size; give <paramref name="visual"/> a <c>RelativeSizeAdjustment</c> of one to fill it.
    /// </summary>
    public static void Add(UIElement element, Visual visual, int order)
    {
        if (!Containers.TryGetValue(element, out var container))
        {
            var root = ElementCompositionPreview.GetElementVisual(element).Compositor.CreateContainerVisual();
            root.RelativeSizeAdjustment = Vector2.One;
            container = new Container(root);
            Containers.Add(element, container);
            ElementCompositionPreview.SetElementChildVisual(element, root);
        }

        container.Insert(visual, order);
    }

    /// <summary>Takes <paramref name="visual"/> away again. The last one removes the container.</summary>
    public static void Remove(UIElement element, Visual visual)
    {
        if (!Containers.TryGetValue(element, out var container) || !container.Remove(visual))
            return;

        if (container.IsEmpty)
        {
            Containers.Remove(element);
            ElementCompositionPreview.SetElementChildVisual(element, null);
            container.Root.Dispose();
        }
    }

    sealed class Container(ContainerVisual root)
    {
        readonly List<(Visual Visual, int Order)> _entries = [];

        public ContainerVisual Root { get; } = root;

        public bool IsEmpty => _entries.Count == 0;

        public void Insert(Visual visual, int order)
        {
            Remove(visual);

            var index = _entries.FindIndex(entry => entry.Order > order);
            if (index < 0)
            {
                Root.Children.InsertAtTop(visual);
                _entries.Add((visual, order));
            }
            else
            {
                Root.Children.InsertBelow(visual, _entries[index].Visual);
                _entries.Insert(index, (visual, order));
            }
        }

        public bool Remove(Visual visual)
        {
            var index = _entries.FindIndex(entry => ReferenceEquals(entry.Visual, visual));
            if (index < 0)
                return false;

            Root.Children.Remove(visual);
            _entries.RemoveAt(index);
            return true;
        }
    }
}
