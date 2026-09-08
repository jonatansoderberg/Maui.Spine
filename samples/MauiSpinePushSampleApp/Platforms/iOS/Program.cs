using Plugin.Maui.Spine.Push;
using UIKit;

namespace MauiSpinePushSampleApp;

public class Program
{
    static void Main(string[] args)
    {
        // Spine.Push adds the remote-notification delegate methods to the AppDelegate class here,
        // because UIKit reads which callbacks the delegate implements when UIApplication.Main assigns
        // it. See docs/wiki/push.md.
        SpinePush.Install();

        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
