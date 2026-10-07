#if IOS || MACCATALYST

using CoreGraphics;
using Foundation;
using Microsoft.Maui.Platform;
using Plugin.Maui.Spine.Core;
using UIKit;

namespace Plugin.Maui.Spine.Extensions;

internal sealed partial class ContextMenuState
{
    UIView? _host;
    UIContextMenuInteraction? _interaction;

    partial void ConnectPlatform(object platformView)
    {
        if (platformView is not UIView host)
            return;

        _host = host;

        // A UIControl has a context menu interaction of its own, driven by its Menu; a second one
        // on the same view would fight it for the long press.
        if (host is UIButton)
            return;

        host.UserInteractionEnabled = true;
        _interaction = new UIContextMenuInteraction(new InteractionDelegate(this));
        host.AddInteraction(_interaction);
    }

    partial void DisconnectPlatform()
    {
        if (_host is UIButton button && MenuButton.GetItems(View) is null)
        {
            button.Menu = null;
            button.ShowsMenuAsPrimaryAction = false;
        }

        if (_interaction is not null)
            _host?.RemoveInteraction(_interaction);

        _interaction = null;
        _host = null;
    }

    partial void UpdatePlatform()
    {
        // The menu button owns a button's menu when it has one.
        if (_host is not UIButton button || MenuButton.GetItems(View) is not null)
            return;

        if (Items is null)
        {
            button.Menu = null;
            return;
        }

        // Built when it opens, like the interaction's menu: an uncached element asks every time.
        var deferred = UIDeferredMenuElement.CreateUncached(completion =>
            completion(CanOpen && Build() is { } menu ? menu.Children : []));

        button.Menu = UIMenu.Create(string.Empty, null, UIMenuIdentifier.None, UIMenuOptions.DisplayInline, [deferred]);
        button.ShowsMenuAsPrimaryAction = false;
    }

    UIMenu? Build() =>
        Items is { } items && View.Handler is { } handler
            ? SpineExtensions.BuildContextMenu(handler, View, items, Parameter)
            : null;

    /// <summary>
    /// The view lifted with its own shape: rounded like a <see cref="Border"/>, and filled with its
    /// background, or the system's, so a transparent row template does not lift as loose content.
    /// </summary>
    UITargetedPreview? Preview()
    {
        if (_host is not { Window: not null } host)
            return null;

        var parameters = new UIPreviewParameters();

        var radius = TapState.CornerRadiusOf(View);
        if (radius > 0)
            parameters.VisiblePath = UIBezierPath.FromRoundedRect(host.Bounds, (nfloat)radius);

        parameters.BackgroundColor = Background() ?? UIColor.SystemBackground;

        return new UITargetedPreview(host, parameters);
    }

    UIColor? Background() => View switch
    {
        { Background: SolidColorBrush { Color: { } color } } => color.ToPlatform(),
        { BackgroundColor: { } color } => color.ToPlatform(),
        _ => null,
    };

    void MenuWillShow() => Tap.GetState(View)?.AbandonPress();

    sealed class InteractionDelegate(ContextMenuState owner) : UIContextMenuInteractionDelegate
    {
        readonly WeakReference<ContextMenuState> _owner = new(owner);

        ContextMenuState? Owner => _owner.TryGetTarget(out var owner) ? owner : null;

        public override UIContextMenuConfiguration? GetConfigurationForMenu(UIContextMenuInteraction interaction, CGPoint location)
        {
            if (Owner is not { CanOpen: true } owner)
                return null;

            return UIContextMenuConfiguration.Create(null, null, _ => owner.Build());
        }

        public override UITargetedPreview? GetHighlightPreview(UIContextMenuInteraction interaction, UIContextMenuConfiguration configuration, INSCopying identifier) =>
            Owner?.Preview();

        public override UITargetedPreview? GetDismissalPreview(UIContextMenuInteraction interaction, UIContextMenuConfiguration configuration, INSCopying identifier) =>
            Owner?.Preview();

        public override void WillDisplayMenu(UIContextMenuInteraction interaction, UIContextMenuConfiguration configuration, IUIContextMenuInteractionAnimating? animator) =>
            Owner?.MenuWillShow();
    }
}

public static partial class SpineExtensions
{
    internal static UIMenu BuildContextMenu(IElementHandler handler, VisualElement owner, MenuItems items, object? parameter) =>
        BuildMenu(handler, owner, items, parameter);
}

#endif
