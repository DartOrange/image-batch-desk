using System.Windows;
namespace ImageBatchDesk;

public partial class App : Application
{
    private async void OnStartup(object sender, StartupEventArgs e)
    {
        if (e.Args.Length == 2 && e.Args[0] == "--self-test")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var code = await Diagnostics.Run(e.Args[1]);
            Shutdown(code);
            return;
        }
        new MainWindow().Show();
    }
}
