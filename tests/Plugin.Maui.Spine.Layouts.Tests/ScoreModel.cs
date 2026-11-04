using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Plugin.Maui.Spine.Layouts.Tests;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Set<T>(ref T field, T value, [CallerMemberName] string name = "")
    {
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>A view model as an app has one: its own properties, a game object of its own, and a list.</summary>
public sealed class ScoreModel : Observable
{
    string _home = "Owls", _away = "Bears";
    Game _game = new();

    public string Home { get => _home; set => Set(ref _home, value); }
    public string Away { get => _away; set => Set(ref _away, value); }
    public Game Game { get => _game; set => Set(ref _game, value); }
    public ObservableCollection<string> Goals { get; } = [];
    public int Unused { get; set; }
}

public sealed class Game : Observable
{
    int _homeGoals, _awayGoals;

    public int HomeGoals { get => _homeGoals; set => Set(ref _homeGoals, value); }
    public int AwayGoals { get => _awayGoals; set => Set(ref _awayGoals, value); }
}
