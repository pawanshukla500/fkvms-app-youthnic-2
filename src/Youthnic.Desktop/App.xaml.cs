using System.Windows;

namespace Youthnic.Desktop;

public partial class App : Application
{
    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
        Environment.Exit(0);
    }
}
