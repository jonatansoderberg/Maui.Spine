using ObjCRuntime;
using Plugin.Maui.Spine.Push;
using UIKit;

namespace Orientera;

public class Program
{
    // This is the main entry point of the application.
    static void Main(string[] args)
    {
        // Måste ske före UIApplication.Main: UIKit läser vilka callbacks delegaten har när den
        // sätts, vilket är för tidigt för UseSpinePush att vara den som lägger till dem.
        SpinePush.Install();

        // if you want to use a different Application Delegate class from "AppDelegate"
        // you can specify it here.
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
