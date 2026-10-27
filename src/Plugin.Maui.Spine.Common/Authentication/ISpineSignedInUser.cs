namespace Plugin.Maui.Spine.Authentication;

/// <summary>
/// The user signed in on this device, as Spine.Authentication knows them. Spine.AI and Spine.Voice read it, so a
/// function run on the device knows the user without the push link (<see cref="ISignedInSession"/>, which is opt-in).
/// </summary>
public interface ISpineSignedInUser
{
    /// <summary>The signed-in user, or <see langword="null"/> when no one is.</summary>
    SpineUser? User { get; }
}
