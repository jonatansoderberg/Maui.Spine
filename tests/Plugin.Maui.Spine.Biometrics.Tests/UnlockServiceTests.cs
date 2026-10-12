using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Plugin.Maui.Spine.Biometrics.Tests;

/// <summary>How <see cref="UnlockService"/> asks, remembers and refuses.</summary>
public class UnlockServiceTests
{
    private sealed class FakeAuth : IBiometricAuth
    {
        public BiometricAvailability Availability { get; set; } = new(BiometricKind.FaceId, Enrolled: true, HasDeviceCredential: true);
        public Queue<BiometricResult> Results { get; } = [];
        public TaskCompletionSource<BiometricResult>? Pending { get; private set; }
        public bool Waits { get; set; }
        public int Prompts { get; private set; }
        public string? LastReason { get; private set; }

        public Task<BiometricAvailability> GetAvailabilityAsync() => Task.FromResult(Availability);

        public Task<BiometricResult> AuthenticateAsync(string reason, BiometricOptions? options = null, CancellationToken cancellationToken = default)
        {
            Prompts++;
            LastReason = reason;

            if (!Waits)
                return Task.FromResult(Results.Count > 0 ? Results.Dequeue() : BiometricResult.Success);

            Pending = new TaskCompletionSource<BiometricResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            return Pending.Task;
        }
    }

    private sealed class FakeMessages : IUnlockMessages
    {
        public List<string> Shown { get; } = [];

        public Task ShowLockedOutAsync()
        {
            Shown.Add("locked out");
            return Task.CompletedTask;
        }

        public Task ShowUnavailableAsync()
        {
            Shown.Add("unavailable");
            return Task.CompletedTask;
        }
    }

    private readonly FakeAuth _auth = new();
    private readonly FakeMessages _messages = new();
    private readonly SpineBiometricsOptions _options = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-12T08:00:00Z"));
    private readonly UnlockService _unlock;

    public UnlockServiceTests() => _unlock = new UnlockService(_auth, _options, _messages, _time);

    [Fact]
    public async Task An_unlock_counts_for_the_grace_period()
    {
        Assert.True(await _unlock.RequireAsync("Open"));
        Assert.True(_unlock.IsUnlocked);

        _time.Advance(TimeSpan.FromSeconds(59));
        Assert.True(await _unlock.RequireAsync("Open again"));
        Assert.Equal(1, _auth.Prompts);

        _time.Advance(TimeSpan.FromSeconds(2));
        Assert.False(_unlock.IsUnlocked);
        Assert.True(await _unlock.RequireAsync("Open later"));
        Assert.Equal(2, _auth.Prompts);
        Assert.Equal("Open later", _auth.LastReason);
    }

    [Fact]
    public async Task A_fresh_request_asks_even_inside_the_grace_period()
    {
        await _unlock.RequireAsync("Open");
        Assert.True(await _unlock.RequireAsync("Delete", fresh: true));

        Assert.Equal(2, _auth.Prompts);
    }

    [Fact]
    public async Task Requests_made_while_a_prompt_shows_share_it()
    {
        _auth.Waits = true;

        var first = _unlock.RequireAsync("Page");
        var second = _unlock.RequireAsync("Shortcut");
        Assert.Same(first, second);

        _auth.Pending!.SetResult(BiometricResult.Success);
        Assert.True(await first);
        Assert.Equal(1, _auth.Prompts);
    }

    [Fact]
    public async Task A_cancelled_prompt_refuses_without_a_message()
    {
        _auth.Results.Enqueue(BiometricResult.Canceled);

        Assert.False(await _unlock.RequireAsync("Open"));
        Assert.False(_unlock.IsUnlocked);
        Assert.Empty(_messages.Shown);
    }

    [Fact]
    public async Task A_lock_out_refuses_with_a_message()
    {
        _auth.Results.Enqueue(BiometricResult.LockedOut);

        Assert.False(await _unlock.RequireAsync("Open"));
        Assert.Equal(["locked out"], _messages.Shown);
    }

    [Fact]
    public async Task Without_a_screen_lock_the_default_lets_it_through_without_asking()
    {
        _auth.Availability = new(BiometricKind.None, Enrolled: false, HasDeviceCredential: false);

        Assert.True(await _unlock.RequireAsync("Open"));
        Assert.Equal(0, _auth.Prompts);
        Assert.Empty(_messages.Shown);
    }

    [Fact]
    public async Task Without_a_screen_lock_deny_refuses_with_a_message()
    {
        _options.WhenUnavailable = UnavailablePolicy.Deny;
        _auth.Availability = new(BiometricKind.Biometric, Enrolled: false, HasDeviceCredential: false);

        Assert.False(await _unlock.RequireAsync("Open"));
        Assert.Equal(["unavailable"], _messages.Shown);
    }

    [Fact]
    public async Task A_missing_usage_key_counts_as_unavailable()
    {
        _options.WhenUnavailable = UnavailablePolicy.Deny;
        _auth.Results.Enqueue(BiometricResult.NotAvailable);

        Assert.False(await _unlock.RequireAsync("Open"));
        Assert.Equal(["unavailable"], _messages.Shown);
    }

    [Fact]
    public async Task Lock_forgets_the_unlock_and_says_so()
    {
        var changes = 0;
        _unlock.LockChanged += (_, _) => changes++;

        await _unlock.RequireAsync("Open");
        _unlock.Lock();
        _unlock.Lock();

        Assert.False(_unlock.IsUnlocked);
        Assert.Equal(2, changes);
        Assert.True(await _unlock.RequireAsync("Open"));
        Assert.Equal(2, _auth.Prompts);
    }

    [Fact]
    public async Task A_device_credential_alone_is_enough_to_ask()
    {
        _auth.Availability = new(BiometricKind.None, Enrolled: false, HasDeviceCredential: true);

        Assert.True(await _unlock.RequireAsync("Open"));
        Assert.Equal(1, _auth.Prompts);
    }
}
