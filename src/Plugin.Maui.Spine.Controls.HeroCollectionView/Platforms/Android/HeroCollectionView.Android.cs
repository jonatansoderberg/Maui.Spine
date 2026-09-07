#if ANDROID
using Android.Views;
using Microsoft.Maui.Controls.Handlers.Items;

namespace Plugin.Maui.Spine.Controls;

public partial class HeroCollectionView
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
