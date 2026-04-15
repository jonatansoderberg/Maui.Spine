namespace Plugin.Maui.Spine.Core;

/// <summary>
/// Represents the typed result of a <see cref="INavigationService.NavigateToWithResultAsync{TPage,TResult}"/> call.
/// </summary>
/// <typeparam name="TResult">The result type declared by the target page.</typeparam>
public sealed record NavigationResult<TResult>
{
    /// <summary>Gets a value indicating whether the navigation returned a result.</summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// Gets the result value when <see cref="IsSuccess"/> is <see langword="true"/>;
    /// otherwise <see langword="default"/>.
    /// </summary>
    public TResult? Value { get; init; }

    private NavigationResult() { }

    /// <summary>Creates a successful result carrying <paramref name="value"/>.</summary>
    public static NavigationResult<TResult> Success(TResult value) =>
        new() { IsSuccess = true, Value = value };

    /// <summary>Creates a canceled (no-result) outcome.</summary>
    public static NavigationResult<TResult> Canceled() =>
        new() { IsSuccess = false, Value = default };
}
