namespace Plugin.Maui.Spine.Common;

/// <summary>Where the app keeps its installation id. Push and AI share it, so the server sees one id per installation.</summary>
internal static class SpineInstallation
{
    /// <summary>The <c>SecureStorage</c> key. Push wrote it first, which is why it says push.</summary>
    public const string IdKey = "spine.push.installation-id";

    /// <summary>The header an app sends its installation id in.</summary>
    public const string Header = "Spine-Installation";
}
