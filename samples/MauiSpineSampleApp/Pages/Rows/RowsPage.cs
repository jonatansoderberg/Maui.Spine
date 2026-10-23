namespace MauiSpineSampleApp.Pages.Rows;

[NavigableRegion(Title = "Rows")]
public partial class RowsPage
{
    public RowsPage() => InitializeComponent();

    private void OnEmailCompleted(object? sender, EventArgs e) => PasswordRow.FocusField();
}
