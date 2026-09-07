using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Server;

/// <summary>Who a push goes to.</summary>
/// <remarks>
/// Every target is a tag expression underneath, except <see cref="Installation"/>, which addresses
/// one registration by id. That mirrors Azure Notification Hubs, where <c>$InstallationId:{id}</c>
/// is a tag; here it is its own case so a register can look it up directly.
/// </remarks>
public sealed class PushTarget
{
    private PushTarget(PushTagExpression? expression, string? installationId)
    {
        Expression = expression;
        InstallationId = installationId;
    }

    /// <summary>The expression to match, or <see langword="null"/> when this targets one installation.</summary>
    public PushTagExpression? Expression { get; }

    /// <summary>The single installation to reach, or <see langword="null"/> when this is an expression.</summary>
    public string? InstallationId { get; }

    /// <summary>Every installation whose tags satisfy <paramref name="expression"/>.</summary>
    /// <param name="expression">A tag expression, in the syntax <see cref="PushTagExpression"/> parses.</param>
    /// <returns>The target.</returns>
    /// <exception cref="FormatException">The expression is not valid.</exception>
    public static PushTarget Tags(string expression) => new(PushTagExpression.Parse(expression), null);

    /// <summary>Every installation whose tags satisfy <paramref name="expression"/>.</summary>
    /// <param name="expression">An already-parsed expression.</param>
    /// <returns>The target.</returns>
    public static PushTarget Tags(PushTagExpression expression) =>
        new(expression ?? throw new ArgumentNullException(nameof(expression)), null);

    /// <summary>One registration, by the id the app registered with.</summary>
    /// <param name="installationId">The installation id.</param>
    /// <returns>The target.</returns>
    public static PushTarget Installation(string installationId) =>
        new(null, installationId ?? throw new ArgumentNullException(nameof(installationId)));

    /// <summary>Every installation tagged <c>user:&lt;id&gt;</c>.</summary>
    /// <param name="userId">The user id.</param>
    /// <returns>The target.</returns>
    public static PushTarget User(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return Tags(PushTagExpression.Parse($"user:{userId}"));
    }

    /// <summary>Every registered installation.</summary>
    public static PushTarget All { get; } = new(PushTagExpression.MatchAll, null);

    /// <inheritdoc />
    public override string ToString() =>
        InstallationId is { } id ? $"installation:{id}" : Expression!.ToString();
}
