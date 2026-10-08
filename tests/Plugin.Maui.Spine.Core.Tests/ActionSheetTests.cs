using CommunityToolkit.Mvvm.Input;
using Plugin.Maui.Spine.Core;
using Xunit;

namespace Plugin.Maui.Spine.Core.Tests;

/// <summary>Which rows an <see cref="ActionSheet"/> shows and what a pick runs.</summary>
public class ActionSheetTests
{
    [Fact]
    public void Takes_the_issue_sketch_as_written()
    {
        var share = new RelayCommand(() => { });
        var sheet = new ActionSheet("Competition")
        {
            Actions =
            [
                new("Share", "Share.svg", share),
                new("Remove", "Trashcan.svg", share) { IsDestructive = true },
            ],
        };

        Assert.Equal("Competition", sheet.Title);
        Assert.Equal(["Share", "Remove"], sheet.Actions.Select(a => a.Title));
        Assert.True(sheet.Actions[1].IsDestructive);
    }

    [Fact]
    public void Leaves_hidden_rows_out_in_order()
    {
        var sheet = new ActionSheet
        {
            Actions =
            {
                new MenuAction("Follow") { IsVisible = false },
                new MenuAction("Unfollow"),
                new MenuAction("Share"),
            },
        };

        Assert.Equal(["Unfollow", "Share"], sheet.VisibleActions.Select(a => a.Title));
    }

    [Fact]
    public void Pick_passes_the_rows_own_parameter_first()
    {
        object? received = null;
        var action = new MenuAction("Remove", null, new RelayCommand<object?>(p => received = p)) { CommandParameter = "row" };
        var sheet = new ActionSheet { CommandParameter = "sheet", Actions = { action } };

        sheet.Pick(action);

        Assert.Equal("row", received);
    }

    [Fact]
    public void Pick_falls_back_to_the_sheets_parameter()
    {
        object? received = null;
        var action = new MenuAction("Remove", null, new RelayCommand<object?>(p => received = p));
        var sheet = new ActionSheet { CommandParameter = "Night sprint", Actions = { action } };

        sheet.Pick(action);

        Assert.Equal("Night sprint", received);
    }

    [Fact]
    public void Pick_respects_CanExecute()
    {
        var ran = false;
        var action = new MenuAction("Remove", null, new RelayCommand(() => ran = true, () => false));
        var sheet = new ActionSheet { Actions = { action } };

        sheet.Pick(action);

        Assert.False(ran);
    }

    [Fact]
    public void Pick_without_a_command_is_just_a_choice()
    {
        var action = new MenuAction("Just a choice");
        var sheet = new ActionSheet { Actions = { action } };

        var error = Record.Exception(() => sheet.Pick(action));

        Assert.Null(error);
    }
}
