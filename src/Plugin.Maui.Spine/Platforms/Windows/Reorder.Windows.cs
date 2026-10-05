using Microsoft.UI.Xaml.Controls;

namespace Plugin.Maui.Spine.Extensions;

internal sealed partial class ReorderState
{
    ListViewBase? _listView;

    partial void ConnectPlatform(object platformView)
    {
        if (platformView is not ListViewBase listView)
            return;

        _listView = listView;
        listView.DragItemsStarting += OnDragItemsStarting;
        listView.DragItemsCompleted += OnDragItemsCompleted;
    }

    partial void DisconnectPlatform()
    {
        if (_listView is not null)
        {
            _listView.DragItemsStarting -= OnDragItemsStarting;
            _listView.DragItemsCompleted -= OnDragItemsCompleted;
        }

        if (!_list.IsSet(Reorder.ModeProperty) || Mode == ReorderMode.Off)
            _list.CanReorderItems = false;

        _listView = null;
    }

    // WinUI's ListView reorders with its own drag, which a mouse starts at once: MAUI's
    // CanReorderItems turns it on, so a handle is the whole item here.
    partial void UpdatePlatform() => _list.CanReorderItems = IsActive;

    void OnDragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        // The list's items are MAUI's template contexts around the source's items.
        var item = e.Items.Count == 1 ? e.Items[0] : null;
        if (item is Microsoft.Maui.Controls.Platform.ItemTemplateContext context)
            item = context.Item;

        if (IndexOf(item) is var index and >= 0)
            BeginDrag(index);
    }

    void OnDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args) => EndDrag();
}
