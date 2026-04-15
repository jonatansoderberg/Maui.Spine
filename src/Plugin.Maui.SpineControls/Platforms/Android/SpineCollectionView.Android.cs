#if ANDROID
using Android.Views;
using Microsoft.Maui.Controls.Handlers.Items;

namespace Plugin.Maui.SpineControls;

public partial class SpineCollectionView
{
    partial void OnHandlerChangedPartial()
    {
        if (Handler is CollectionViewHandler handler &&
            handler.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView rv)
        {
            rv.OverScrollMode = OverScrollMode.Never;
        }
    }
}
#endif
