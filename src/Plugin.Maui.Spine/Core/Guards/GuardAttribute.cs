namespace Plugin.Maui.Spine.Core;

/// <summary>
/// The base of an attribute that has to pass before Spine shows a page or runs a command: an unlock,
/// a sign-in. A package derives its own attribute from it and registers the matching
/// <see cref="IGuard{TAttribute}"/> with <see cref="SpineGuardServiceCollectionExtensions.AddSpineGuard{TAttribute, TGuard}"/>.
/// </summary>
/// <remarks>
/// <para>
/// On a page class it runs before every navigation to the page: a push, a sheet, a tab switch,
/// <c>ShowAsync</c>, a result navigation and <c>SetRootAsync</c>. The page and its view model are not
/// created until every guard has passed, so a refused page costs nothing.
/// </para>
/// <para>
/// On a <c>[RelayCommand]</c> method or an <see cref="System.Windows.Input.ICommand"/> property of a page's
/// view model it runs before the command, wherever Spine runs it: a page action, a menu or context
/// menu row, an action sheet row, <c>Tap.Command</c> and <c>{PageCommand}</c>. A plain
/// <c>Command="{Binding DeleteCommand}"</c> hands the command to the button directly and is not guarded;
/// a guard is a gate in the UI, not a lock on the code.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Property, Inherited = true)]
public abstract class GuardAttribute : Attribute
{
    /// <summary>
    /// Lower runs first when a target has several guards. Spine's own packages use 100 for sign-in and
    /// 200 for unlock, so a user is signed in before being asked to unlock.
    /// </summary>
    public int Order { get; init; }
}
