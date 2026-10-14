using Microsoft.Extensions.DependencyInjection.Extensions;
using Plugin.Maui.Spine.Services;

namespace Plugin.Maui.Spine.Core;

/// <summary>Registers guards for <see cref="GuardAttribute"/>s.</summary>
public static class SpineGuardServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TGuard"/> as the singleton that answers for <typeparamref name="TAttribute"/>,
    /// on pages and on commands. A second call for the same attribute does nothing.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddSpineGuard&lt;RequiresUnlockAttribute, UnlockGuard&gt;();
    /// </code>
    /// </example>
    public static IServiceCollection AddSpineGuard<TAttribute, TGuard>(this IServiceCollection services)
        where TAttribute : GuardAttribute
        where TGuard : class, IGuard<TAttribute>
    {
        if (services.Any(static d => d.ImplementationInstance is GuardRegistration r && r.AttributeType == typeof(TAttribute)))
            return services;

        services.TryAddSingleton<TGuard>();
        services.TryAddSingleton<IGuard<TAttribute>>(static sp => sp.GetRequiredService<TGuard>());
        services.AddSingleton(new GuardRegistration(
            typeof(TAttribute),
            static sp => sp.GetRequiredService<IGuard<TAttribute>>(),
            static (guard, attribute, context, token) => ((IGuard<TAttribute>)guard).CheckAsync((TAttribute)attribute, context, token)));

        return services;
    }
}
