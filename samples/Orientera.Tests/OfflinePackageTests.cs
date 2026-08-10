using Orientera.Services.FakeData;
using Orientera.Services.Offline;
using Orientera.Services.Sources;
using Orientera.Services.Time;

namespace Orientera.Tests;

/// <summary>
/// The offline path exists so that bad coverage at an arena cannot take out the PM, the start
/// time or the arena details. These pin the behaviour that promise rests on.
/// </summary>
public class OfflinePackageTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "orientera-offline-tests", Guid.NewGuid().ToString("N"));

    private readonly TimeMachineClock _clock = new(FakeDataset.DefaultNow);
    private readonly ConnectivitySwitch _connectivity = new();
    private readonly FileOfflineStore _store;
    private readonly UnreliableSource _source;
    private readonly OfflinePackageService _service;

    public OfflinePackageTests()
    {
        _store = new FileOfflineStore(_directory);
        _source = new UnreliableSource(new FakeDataSource(_clock), _connectivity);
        _service = new OfflinePackageService(_clock, _source, _source, _source, _store);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task A_reachable_source_serves_live_data()
    {
        var snapshot = await _service.GetAsync(FakeDataset.NmLongId);

        Assert.Equal(DataOrigin.Live, snapshot.Origin);
        Assert.NotNull(snapshot.Competition);
        Assert.Null(snapshot.CachedAt);
    }

    [Fact]
    public async Task Reading_a_competition_also_stores_it()
    {
        await _service.GetAsync(FakeDataset.NmLongId);

        var stored = await _store.GetAsync(FakeDataset.NmLongId);

        Assert.NotNull(stored);
        Assert.Equal(FakeDataset.NmLongId, stored.Competition.Id);
    }

    [Fact]
    public async Task An_unreachable_source_falls_back_to_the_stored_package()
    {
        await _service.GetAsync(FakeDataset.NmLongId);

        _connectivity.IsOffline = true;
        var snapshot = await _service.GetAsync(FakeDataset.NmLongId);

        Assert.Equal(DataOrigin.Cache, snapshot.Origin);
        Assert.Equal(FakeDataset.NmLongId, snapshot.Competition?.Id);
        Assert.NotNull(snapshot.CachedAt);
    }

    [Fact]
    public async Task The_cached_package_keeps_what_matters_at_the_arena()
    {
        await _service.GetAsync(FakeDataset.NmLongId);
        _connectivity.IsOffline = true;

        var snapshot = await _service.GetAsync(FakeDataset.NmLongId);

        // My start time, the PM's structured content and the arena all survive the outage.
        Assert.NotNull(snapshot.MyStart);
        Assert.NotNull(snapshot.Competition?.Profile);
        Assert.NotEmpty(snapshot.Competition!.Place);
        Assert.NotEmpty(snapshot.Competition.Profile!.Facts);
    }

    [Fact]
    public async Task Nothing_cached_and_no_connection_reports_unavailable_rather_than_throwing()
    {
        _connectivity.IsOffline = true;

        var snapshot = await _service.GetAsync(FakeDataset.DmSprintId);

        Assert.Equal(DataOrigin.Unavailable, snapshot.Origin);
        Assert.Null(snapshot.Competition);
    }

    [Fact]
    public async Task Refresh_stores_what_I_am_entered_in_and_what_I_starred()
    {
        var saved = await _service.RefreshRelevantAsync();

        Assert.True(saved > 0);

        // Entered.
        Assert.NotNull(await _store.GetAsync(FakeDataset.NmLongId));

        // Starred but not entered — DM Sprint is seeded as a favourite.
        Assert.NotNull(await _store.GetAsync(FakeDataset.DmSprintId));
    }

    [Fact]
    public async Task Refresh_without_a_connection_leaves_the_stored_packages_alone()
    {
        await _service.RefreshRelevantAsync();
        var before = await _store.GetAsync(FakeDataset.NmLongId);

        _connectivity.IsOffline = true;
        var saved = await _service.RefreshRelevantAsync();

        var after = await _store.GetAsync(FakeDataset.NmLongId);

        Assert.Equal(0, saved);
        Assert.Equal(before!.CachedAt, after!.CachedAt);
    }

    [Fact]
    public async Task A_package_survives_a_new_store_over_the_same_directory()
    {
        await _service.GetAsync(FakeDataset.NmLongId);

        // A fresh store stands in for the next app launch.
        var reopened = new FileOfflineStore(_directory);
        var stored = await reopened.GetAsync(FakeDataset.NmLongId);

        Assert.NotNull(stored);
        Assert.Equal("Norrlandsmästerskapen Lång", stored.Competition.Name);
    }

    [Fact]
    public async Task A_corrupt_package_is_discarded_rather_than_failing_every_read()
    {
        await _service.GetAsync(FakeDataset.NmLongId);

        var file = Directory.EnumerateFiles(_directory, "*.json").Single();
        await File.WriteAllTextAsync(file, "{ this is not json");

        Assert.Null(await _store.GetAsync(FakeDataset.NmLongId));
        Assert.False(File.Exists(file));
    }

    [Fact]
    public async Task Local_data_keeps_working_without_a_connection()
    {
        _connectivity.IsOffline = true;

        // Who I am, who I follow and what I starred are local — an outage must not hide them.
        Assert.NotNull(await _source.GetMeAsync());
        Assert.NotEmpty(await _source.GetMyGroupAsync());
        Assert.NotEmpty(await _source.GetFavouritesAsync());
    }

    [Fact]
    public async Task A_remote_read_fails_loudly_when_offline()
    {
        _connectivity.IsOffline = true;

        await Assert.ThrowsAsync<SourceUnavailableException>(() => _source.GetCompetitionsAsync());
    }
}
