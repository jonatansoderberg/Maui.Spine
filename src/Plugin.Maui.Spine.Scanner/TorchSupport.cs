namespace Plugin.Maui.Spine.Scanner;

/// <summary>
/// Whether the camera a scanner uses has a torch, known before the camera starts: a sheet's header is settled before
/// it shows, so the torch and the close button never move once the sheet is up.
/// </summary>
internal static class TorchSupport
{
    public static bool IsSupported
    {
        get
        {
            try
            {
#if IOS || MACCATALYST
                return AVFoundation.AVCaptureDevice.GetDefaultDevice(
                    AVFoundation.AVCaptureDeviceType.BuiltInWideAngleCamera, AVFoundation.AVMediaTypes.Video,
                    AVFoundation.AVCaptureDevicePosition.Back)?.HasTorch == true;
#elif ANDROID
                return Android.App.Application.Context.PackageManager?.HasSystemFeature(Android.Content.PM.PackageManager.FeatureCameraFlash) == true;
#else
                return false;
#endif
            }
            catch
            {
                return false;
            }
        }
    }
}
