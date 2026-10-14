using System.Collections.Concurrent;
using System.Reflection;
using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Services;

/// <summary>A guard registered for one attribute type by <see cref="SpineGuardServiceCollectionExtensions.AddSpineGuard{TAttribute, TGuard}"/>.</summary>
internal sealed record GuardRegistration(
    Type AttributeType,
    Func<IServiceProvider, object> Resolve,
    Func<object, GuardAttribute, GuardContext, CancellationToken, ValueTask<bool>> Check);

/// <summary>
/// Runs the <see cref="GuardAttribute"/>s of a page type or a command, in <see cref="GuardAttribute.Order"/>,
/// until one refuses.
/// </summary>
internal sealed class GuardRunner
{
    private static readonly ConcurrentDictionary<MemberInfo, GuardAttribute[]> _attributes = new();

    private readonly IServiceProvider _services;
    private readonly Dictionary<Type, GuardRegistration> _registrations;
    private readonly SpineReadiness _readiness;
    private readonly HashSet<Type> _pendingPages = [];
    private CancellationTokenSource _cancellation = new();

    public GuardRunner(IServiceProvider services, IEnumerable<GuardRegistration> registrations, SpineReadiness readiness)
    {
        _services = services;
        _readiness = readiness;
        _registrations = [];

        foreach (var registration in registrations)
            _registrations[registration.AttributeType] = registration;
    }

    /// <summary>The guard attributes on <paramref name="member"/>, in the order they run. Read once per member.</summary>
    public static GuardAttribute[] AttributesOf(MemberInfo member) =>
        _attributes.GetOrAdd(member, static m => m
            .GetCustomAttributes(typeof(GuardAttribute), inherit: true)
            .Cast<GuardAttribute>()
            .OrderBy(a => a.Order)
            .ThenBy(a => a.GetType().FullName, StringComparer.Ordinal)
            .ToArray());

    /// <summary>Whether <paramref name="page"/> has any guard.</summary>
    public static bool HasGuards(Type page) => AttributesOf(page).Length > 0;

    /// <summary>The guards of <paramref name="page"/> with their attributes, for a tab host that asks one for a cover.</summary>
    public IEnumerable<(GuardAttribute Attribute, object Guard)> GuardsOf(Type page)
    {
        foreach (var attribute in AttributesOf(page))
            yield return (attribute, Registration(attribute, page.Name).Resolve(_services));
    }

    /// <summary>
    /// Runs the guards of <paramref name="page"/>. Completes at once with <see langword="true"/> when it has
    /// none. A navigation to a page whose guards are already being asked is dropped, so a double tap gives
    /// one prompt and one navigation.
    /// </summary>
    public ValueTask<bool> PassPageAsync(Type page, GuardTrigger trigger, bool canCancel = true)
    {
        var attributes = AttributesOf(page);
        if (attributes.Length == 0)
            return ValueTask.FromResult(true);

        foreach (var attribute in attributes)
            Registration(attribute, page.Name);

        lock (_pendingPages)
        {
            if (!_pendingPages.Add(page))
                return ValueTask.FromResult(false);
        }

        return PassPageCoreAsync(page, attributes, new GuardContext(trigger, page, null, canCancel));
    }

    private async ValueTask<bool> PassPageCoreAsync(Type page, GuardAttribute[] attributes, GuardContext context)
    {
        try
        {
            return await RunAsync(attributes, context, page.Name);
        }
        finally
        {
            lock (_pendingPages)
                _pendingPages.Remove(page);
        }
    }

    /// <summary>Runs <paramref name="attributes"/> for the command <paramref name="command"/>.</summary>
    public ValueTask<bool> PassCommandAsync(GuardAttribute[] attributes, string command)
    {
        if (attributes.Length == 0)
            return ValueTask.FromResult(true);

        foreach (var attribute in attributes)
            Registration(attribute, command);

        return RunAsync(attributes, new GuardContext(GuardTrigger.Command, null, command, CanCancel: true), command);
    }

    /// <summary>
    /// Cancels every guard still asking, for example when the host is swapped on sign-out, so a pending
    /// prompt does not lead into a page of the old session.
    /// </summary>
    public void CancelPending()
    {
        var previous = Interlocked.Exchange(ref _cancellation, new CancellationTokenSource());
        previous.Cancel();
        previous.Dispose();
    }

    private async ValueTask<bool> RunAsync(GuardAttribute[] attributes, GuardContext context, string target)
    {
        var token = _cancellation.Token;

        // A prompt cannot be shown before the app is active, and a page shown before the root would be
        // replaced by it: a guarded page opened from a shortcut on a cold start waits for both.
        await _readiness.WhenActivated.WaitAsync(token);
        if (context.Trigger is not GuardTrigger.Root)
            await _readiness.WhenRootSet.WaitAsync(token);

        foreach (var attribute in attributes)
        {
            var registration = Registration(attribute, target);
            bool passed;

            try
            {
                passed = await registration.Check(registration.Resolve(_services), attribute, context, token);
            }
            catch (OperationCanceledException)
            {
                passed = false;
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"[Spine] Guard [{Name(attribute)}] on {target} failed, which counts as a refusal: {e}");
                passed = false;
            }

            if (!passed)
            {
                System.Diagnostics.Debug.WriteLine($"[Spine] Guard [{Name(attribute)}] refused {context.Trigger} {target}.");
                return false;
            }
        }

        return true;
    }

    private GuardRegistration Registration(GuardAttribute attribute, string target)
    {
        for (var type = attribute.GetType(); type is not null && type != typeof(GuardAttribute); type = type.BaseType)
        {
            if (_registrations.TryGetValue(type, out var registration))
                return registration;
        }

        throw new InvalidOperationException(
            $"[{Name(attribute)}] on {target} has no guard registered, so Spine cannot let it through. " +
            $"Reference the package that defines {attribute.GetType().Name} and set it up in MauiProgram, " +
            $"or register a guard with AddSpineGuard<{attribute.GetType().Name}, TGuard>().");
    }

    private static string Name(GuardAttribute attribute)
    {
        var name = attribute.GetType().Name;
        return name.EndsWith("Attribute", StringComparison.Ordinal) ? name[..^"Attribute".Length] : name;
    }
}
