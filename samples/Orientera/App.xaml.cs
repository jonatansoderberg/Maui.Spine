using Orientera.Services.Theming;

namespace Orientera;

public partial class App
{
    public App()
    {
        InitializeComponent();
        ThemeManager.Attach(this);
    }
}
