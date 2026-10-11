using CommunityToolkit.Mvvm.Input;
using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Services;
using Xunit;

namespace Plugin.Maui.Spine.Core.Tests;

/// <summary>How guards run before a page is shown or a command runs.</summary>
public partial class GuardTests
{
    private class AskAttribute : GuardAttribute;

    private sealed class SecondAskAttribute : GuardAttribute;

    private sealed class StrictAskAttribute : AskAttribute;

    private sealed class UnregisteredAttribute : GuardAttribute;

    /// <summary>A guard whose answer the test gives, and which records the order it was asked in.</summary>
    private sealed class TestGuard<TAttribute>(List<string> log, string name) : IGuard<TAttribute>
        where TAttribute : GuardAttribute
    {
        public TaskCompletionSource<bool>? Pending { get; private set; }
        public Func<bool> Answer { get; set; } = static () => true;
        public bool Waits { get; set; }
        public Exception? Throws { get; set; }
        public GuardContext? LastContext { get; private set; }

        public ValueTask<bool> CheckAsync(TAttribute attribute, GuardContext context, CancellationToken cancellationToken)
        {
            log.Add(name);
            LastContext = context;

            if (Throws is not null)
                throw Throws;

            if (!Waits)
                return ValueTask.FromResult(Answer());

            Pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() => Pending.TrySetCanceled(cancellationToken));
            return new ValueTask<bool>(Pending.Task);
        }
    }

    private sealed class Services : IServiceProvider
    {
        private readonly Dictionary<Type, object> _services = [];

        public void Add<T>(T service) where T : notnull => _services[typeof(T)] = service;

        public object? GetService(Type serviceType) => _services.GetValueOrDefault(serviceType);
    }

    [Ask]
    private sealed class AskPage;

    [SecondAsk(Order = 300), Ask(Order = 200)]
    private sealed class TwoGuardsPage;

    [StrictAsk]
    private sealed class StrictPage;

    [Unregistered]
    private sealed class UnregisteredPage;

    private sealed class OpenPage;

    private readonly List<string> _log = [];
    private readonly TestGuard<AskAttribute> _ask;
    private readonly TestGuard<SecondAskAttribute> _second;
    private readonly SpineReadiness _readiness = new();
    private readonly GuardRunner _runner;

    public GuardTests()
    {
        _ask = new(_log, "ask");
        _second = new(_log, "second");

        var services = new Services();
        services.Add<IGuard<AskAttribute>>(_ask);
        services.Add<IGuard<SecondAskAttribute>>(_second);

        _runner = new GuardRunner(services, [Registration<AskAttribute>(), Registration<SecondAskAttribute>()], _readiness);
        _readiness.MarkActivated();
        _readiness.MarkRootSet();
        CommandGuards.Runner = () => _runner;
    }

    private static GuardRegistration Registration<TAttribute>() where TAttribute : GuardAttribute => new(
        typeof(TAttribute),
        sp => sp.GetService(typeof(IGuard<TAttribute>))!,
        (guard, attribute, context, token) => ((IGuard<TAttribute>)guard).CheckAsync((TAttribute)attribute, context, token));

    [Fact]
    public void A_page_without_guards_passes_at_once()
    {
        var pass = _runner.PassPageAsync(typeof(OpenPage), GuardTrigger.Push);

        Assert.True(pass.IsCompletedSuccessfully);
        Assert.True(pass.Result);
        Assert.Empty(_log);
    }

    [Fact]
    public async Task Guards_run_in_order()
    {
        Assert.True(await _runner.PassPageAsync(typeof(TwoGuardsPage), GuardTrigger.Push));
        Assert.Equal(["ask", "second"], _log);
        Assert.Equal(new GuardContext(GuardTrigger.Push, typeof(TwoGuardsPage), null, true), _ask.LastContext);
    }

    [Fact]
    public async Task The_first_refusal_stops_the_rest()
    {
        _ask.Answer = static () => false;

        Assert.False(await _runner.PassPageAsync(typeof(TwoGuardsPage), GuardTrigger.Sheet));
        Assert.Equal(["ask"], _log);
    }

    [Fact]
    public async Task A_guard_that_throws_refuses()
    {
        _ask.Throws = new InvalidOperationException("boom");

        Assert.False(await _runner.PassPageAsync(typeof(AskPage), GuardTrigger.Push));
    }

    [Fact]
    public void An_attribute_without_a_guard_throws_and_names_it()
    {
        var e = Assert.Throws<InvalidOperationException>(() => _runner.PassPageAsync(typeof(UnregisteredPage), GuardTrigger.Push));

        Assert.Contains("[Unregistered]", e.Message);
        Assert.Contains("AddSpineGuard<UnregisteredAttribute", e.Message);
    }

    [Fact]
    public async Task A_derived_attribute_uses_the_guard_of_its_base()
    {
        // No registration for StrictAskAttribute itself, but one for AskAttribute, which it derives from.
        Assert.True(await _runner.PassPageAsync(typeof(StrictPage), GuardTrigger.Push));
        Assert.Equal(["ask"], _log);
    }

    [Fact]
    public async Task A_second_navigation_while_the_first_asks_is_dropped()
    {
        _ask.Waits = true;

        var first = _runner.PassPageAsync(typeof(AskPage), GuardTrigger.Push).AsTask();
        var second = await _runner.PassPageAsync(typeof(AskPage), GuardTrigger.Push);

        Assert.False(second);
        _ask.Pending!.SetResult(true);
        Assert.True(await first);
        Assert.Single(_log);

        // Once the first is done, the page can be asked for again.
        _ask.Waits = false;
        Assert.True(await _runner.PassPageAsync(typeof(AskPage), GuardTrigger.Push));
    }

    [Fact]
    public async Task Guards_wait_until_the_app_is_active_and_the_root_is_set()
    {
        var readiness = new SpineReadiness();
        var services = new Services();
        services.Add<IGuard<AskAttribute>>(_ask);
        var runner = new GuardRunner(services, [Registration<AskAttribute>()], readiness);

        var push = runner.PassPageAsync(typeof(AskPage), GuardTrigger.Push).AsTask();
        await Task.Delay(20);
        Assert.Empty(_log);

        readiness.MarkActivated();
        await Task.Delay(20);
        Assert.Empty(_log);

        readiness.MarkRootSet();
        Assert.True(await push);
        Assert.Equal(["ask"], _log);
    }

    [Fact]
    public async Task A_root_guard_waits_for_activation_only_and_cannot_cancel()
    {
        var readiness = new SpineReadiness();
        var services = new Services();
        services.Add<IGuard<AskAttribute>>(_ask);
        var runner = new GuardRunner(services, [Registration<AskAttribute>()], readiness);

        var root = runner.PassPageAsync(typeof(AskPage), GuardTrigger.Root, canCancel: false).AsTask();
        readiness.MarkActivated();

        Assert.True(await root);
        Assert.False(_ask.LastContext!.CanCancel);
    }

    [Fact]
    public async Task Cancelling_pending_guards_refuses_them()
    {
        _ask.Waits = true;

        var push = _runner.PassPageAsync(typeof(AskPage), GuardTrigger.Push).AsTask();
        await Task.Delay(20);
        _runner.CancelPending();

        Assert.False(await push);
    }

    private sealed partial class AccountViewModel
    {
        public int Deleted { get; private set; }
        public int Shared { get; private set; }

        [RelayCommand, Ask]
        private void Delete() => Deleted++;

        [RelayCommand]
        private void Share() => Shared++;
    }

    private sealed partial class BrokenViewModel
    {
        [Ask]
        private void NotACommand() { }
    }

    [Fact]
    public async Task A_guarded_command_runs_once_its_guard_passes()
    {
        var viewModel = new AccountViewModel();
        CommandGuards.Discover(viewModel);
        _ask.Waits = true;

        Assert.True(CommandGuards.IsGuarded(viewModel.DeleteCommand));

        CommandGuards.Execute(viewModel.DeleteCommand, null);
        await Task.Delay(20);
        Assert.Equal(0, viewModel.Deleted);
        Assert.Equal(new GuardContext(GuardTrigger.Command, null, "AccountViewModel.DeleteCommand", true), _ask.LastContext);

        _ask.Pending!.SetResult(true);
        await Task.Delay(20);
        Assert.Equal(1, viewModel.Deleted);
    }

    [Fact]
    public async Task A_refused_command_does_not_run()
    {
        var viewModel = new AccountViewModel();
        CommandGuards.Discover(viewModel);
        _ask.Answer = static () => false;

        CommandGuards.Execute(viewModel.DeleteCommand, null);
        await Task.Delay(20);

        Assert.Equal(0, viewModel.Deleted);
    }

    [Fact]
    public async Task A_command_waiting_on_its_guard_ignores_more_taps()
    {
        var viewModel = new AccountViewModel();
        CommandGuards.Discover(viewModel);
        _ask.Waits = true;

        CommandGuards.Execute(viewModel.DeleteCommand, null);
        CommandGuards.Execute(viewModel.DeleteCommand, null);
        await Task.Delay(20);
        _ask.Pending!.SetResult(true);
        await Task.Delay(20);

        Assert.Single(_log);
        Assert.Equal(1, viewModel.Deleted);
    }

    [Fact]
    public void An_unguarded_command_runs_at_once()
    {
        var viewModel = new AccountViewModel();
        CommandGuards.Discover(viewModel);

        Assert.False(CommandGuards.IsGuarded(viewModel.ShareCommand));
        CommandGuards.Execute(viewModel.ShareCommand, null);

        Assert.Equal(1, viewModel.Shared);
        Assert.Empty(_log);
    }

    [Fact]
    public async Task A_wrapped_command_asks_when_it_runs_and_keeps_its_identity()
    {
        var viewModel = new AccountViewModel();

        // Wrapped before the view model is scanned, as a binding inside the page can be.
        var wrapped = CommandGuards.Wrap(viewModel.DeleteCommand);
        Assert.Same(wrapped, CommandGuards.Wrap(viewModel.DeleteCommand));

        CommandGuards.Discover(viewModel);
        _ask.Answer = static () => false;

        Assert.True(wrapped.CanExecute(null));
        wrapped.Execute(null);
        await Task.Delay(20);

        Assert.Equal(["ask"], _log);
        Assert.Equal(0, viewModel.Deleted);
    }

    [Fact]
    public void A_guard_on_a_method_without_a_command_throws()
    {
        var e = Assert.Throws<InvalidOperationException>(() => CommandGuards.Discover(new BrokenViewModel()));

        Assert.Contains("NotACommandCommand", e.Message);
    }

    [Fact]
    public async Task An_action_sheet_row_asks_the_guard_of_its_command()
    {
        var viewModel = new AccountViewModel();
        CommandGuards.Discover(viewModel);
        var sheet = new ActionSheet("Account") { Actions = [new("Delete", null, viewModel.DeleteCommand)] };

        sheet.Pick(sheet.Actions[0]);
        await Task.Delay(20);

        Assert.Equal(["ask"], _log);
        Assert.Equal(1, viewModel.Deleted);
    }
}
