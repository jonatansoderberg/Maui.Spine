using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Services;

/// <summary>
/// The guards of the commands on a page's view model. A command is found once per view model instance by
/// <see cref="Discover"/>; the places Spine runs commands then go through <see cref="Execute"/> or
/// <see cref="Wrap"/>, which ask the command's guards first.
/// </summary>
/// <remarks>
/// Commands are looked up by instance, so a command shared by a menu row and a header-bar button is guarded
/// in both. A command Spine never sees (a plain <c>Command="{Binding XCommand}"</c>, a row's own view model)
/// is not guarded.
/// </remarks>
internal static class CommandGuards
{
    private const BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private sealed record Template(PropertyInfo Command, GuardAttribute[] Attributes);

    private sealed class Entry(GuardAttribute[] attributes, string name)
    {
        public GuardAttribute[] Attributes { get; } = attributes;
        public string Name { get; } = name;
        public bool Pending { get; set; }
    }

    private static readonly ConcurrentDictionary<Type, Template[]> _templates = new();
    private static readonly ConditionalWeakTable<ICommand, Entry> _guarded = new();
    private static readonly ConditionalWeakTable<ICommand, ICommand> _wrappers = new();

    /// <summary>Finds the <see cref="GuardRunner"/>; set by <c>UseSpine</c>, and by tests.</summary>
    internal static Func<GuardRunner?> Runner { get; set; } = static () => null;

    /// <summary>
    /// Records the guarded commands of <paramref name="viewModel"/>: guard attributes on <c>[RelayCommand]</c>
    /// methods and on <see cref="ICommand"/> properties.
    /// </summary>
    public static void Discover(object viewModel)
    {
        var type = viewModel.GetType();

        foreach (var template in _templates.GetOrAdd(type, Scan))
        {
            if (template.Command.GetValue(viewModel) is ICommand command && !_guarded.TryGetValue(command, out _))
                _guarded.AddOrUpdate(command, new Entry(template.Attributes, $"{type.Name}.{template.Command.Name}"));
        }
    }

    /// <summary>Whether <paramref name="command"/> has guards.</summary>
    public static bool IsGuarded(ICommand command) => _guarded.TryGetValue(command, out _);

    /// <summary>
    /// A command for a control that runs <paramref name="command"/> itself, such as a header-bar button: it
    /// goes through <see cref="Execute"/>, so the guards are looked up when it runs, not when it is bound.
    /// A binding can be evaluated before the page's view model has been scanned. The same wrapper is
    /// returned for the same command.
    /// </summary>
    [return: NotNullIfNotNull(nameof(command))]
    public static ICommand? Wrap(ICommand? command) =>
        command is null ? null : _wrappers.GetValue(command, static c => new GuardedCommand(c));

    /// <summary>
    /// Runs <paramref name="command"/> when it can execute: at once when it has no guards, otherwise once its
    /// guards have passed. A command waiting on its guards ignores further executions.
    /// </summary>
    public static void Execute(ICommand command, object? parameter)
    {
        if (!_guarded.TryGetValue(command, out var entry))
        {
            if (command.CanExecute(parameter))
                command.Execute(parameter);

            return;
        }

        if (command.CanExecute(parameter))
            _ = ExecuteGuardedAsync(command, entry, parameter);
    }

    private static async Task ExecuteGuardedAsync(ICommand command, Entry entry, object? parameter)
    {
        if (entry.Pending)
            return;

        entry.Pending = true;

        try
        {
            if (Runner() is not { } runner)
            {
                System.Diagnostics.Debug.WriteLine($"[Spine] {entry.Name} has guards but no GuardRunner is set up; the command does not run.");
                return;
            }

            if (await runner.PassCommandAsync(entry.Attributes, entry.Name) && command.CanExecute(parameter))
                command.Execute(parameter);
        }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine($"[Spine] {entry.Name}: {e}");
        }
        finally
        {
            entry.Pending = false;
        }
    }

    private static Template[] Scan(Type type)
    {
        var found = new List<Template>();

        foreach (var member in type.GetMembers(MemberFlags))
        {
            if (member is not (MethodInfo or PropertyInfo))
                continue;

            var attributes = GuardRunner.AttributesOf(member);
            if (attributes.Length == 0)
                continue;

            var commandName = member switch
            {
                PropertyInfo property when typeof(ICommand).IsAssignableFrom(property.PropertyType) => property.Name,
                MethodInfo method => ToolkitNames.CommandFor(method.Name),
                _ => member.Name,
            };

            var command = type.GetProperty(commandName, MemberFlags);
            if (command is null || !typeof(ICommand).IsAssignableFrom(command.PropertyType))
                throw new InvalidOperationException(
                    $"A guard on {type.Name}.{member.Name} needs a command property named '{commandName}'. " +
                    "Put the guard on a [RelayCommand] method or an ICommand property.");

            found.Add(new Template(command, attributes));
        }

        return [.. found];
    }

    private sealed class GuardedCommand(ICommand inner) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add => inner.CanExecuteChanged += value;
            remove => inner.CanExecuteChanged -= value;
        }

        public bool CanExecute(object? parameter) => inner.CanExecute(parameter);

        public void Execute(object? parameter) => CommandGuards.Execute(inner, parameter);
    }
}
