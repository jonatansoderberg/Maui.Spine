using Microsoft.Maui.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Grid = Microsoft.UI.Xaml.Controls.Grid;

namespace Plugin.Maui.Spine.Core;

public static partial class SpineOverlay
{
    /// <summary>Puts the overlays back on top after a sheet's popup has opened over them.</summary>
    internal static void Raise()
    {
        foreach (var handle in _shown)
            handle.Raise();
    }

    private sealed partial class Handle
    {
        private Popup? _popup;
        private XamlRoot? _root;
        private Grid? _host;

        /// <remarks>A popup on the window's <see cref="XamlRoot"/>, as the sheet uses; a later popup is drawn over an earlier one.</remarks>
        public partial void Attach(IMauiContext context)
        {
            if (_window.Handler?.PlatformView is not Microsoft.UI.Xaml.Window { Content.XamlRoot: { } root })
                return;

            _host = new Grid { Width = root.Size.Width, Height = root.Size.Height };
            _host.Children.Add(_content.ToPlatform(context));

            _root = root;
            _root.Changed += OnRootChanged;

            _popup = new Popup { XamlRoot = root, Child = _host, IsLightDismissEnabled = false };
            _popup.IsOpen = true;
        }

        public void Raise()
        {
            if (_popup is not { IsOpen: true } popup)
                return;

            popup.IsOpen = false;
            popup.IsOpen = true;
        }

        private partial void Detach()
        {
            _root?.Changed -= OnRootChanged;
            _popup?.IsOpen = false;
            _popup?.Child = null;
            _host?.Children.Clear();
            _popup = null;
            _root = null;
            _host = null;
        }

        private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
        {
            if (_host is null)
                return;

            _host.Width = sender.Size.Width;
            _host.Height = sender.Size.Height;
        }
    }
}
