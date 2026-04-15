using MauiSpineSampleApp.Pages;

namespace MauiBottomSheetPoc;

public partial class ContextItem : ContentView
{
    public ContextItem()
    {
        InitializeComponent();
    }

    // Bindable property for the item
    public static readonly BindableProperty ItemProperty =
        BindableProperty.Create(nameof(Item), typeof(Item), typeof(ContextItem));

    public Item Item
    {
        get => (Item)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    // Commands
    public static readonly BindableProperty EditCommandProperty =
        BindableProperty.Create(nameof(EditCommand), typeof(Command), typeof(ContextItem));

    public Command EditCommand
    {
        get => (Command)GetValue(EditCommandProperty);
        set => SetValue(EditCommandProperty, value);
    }

    public static readonly BindableProperty MoveCommandProperty =
        BindableProperty.Create(nameof(MoveCommand), typeof(Command), typeof(ContextItem));

    public Command MoveCommand
    {
        get => (Command)GetValue(MoveCommandProperty);
        set => SetValue(MoveCommandProperty, value);
    }

    public static readonly BindableProperty DeleteCommandProperty =
        BindableProperty.Create(nameof(DeleteCommand), typeof(Command), typeof(ContextItem));

    public Command DeleteCommand
    {
        get => (Command)GetValue(DeleteCommandProperty);
        set => SetValue(DeleteCommandProperty, value);
    }

}