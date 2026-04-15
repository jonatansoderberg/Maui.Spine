namespace Plugin.Maui.Spine.Core;

/// <summary>
/// Marker interface that identifies a page as navigable by <see cref="INavigationService"/>.
/// All pages decorated with <see cref="NavigableRegionAttribute"/> or <see cref="NavigableSheetAttribute"/>
/// implicitly implement this interface through <see cref="SpinePage{TViewModel}"/>.
/// </summary>
public interface INavigable {
}
