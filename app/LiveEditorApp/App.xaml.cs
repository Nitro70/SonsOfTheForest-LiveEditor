using System.Windows;
using System.Windows.Threading;

namespace LiveEditorApp;

public partial class App : Application
{
    /// <summary>
    /// Every UI handler here is `async void`, so an unhandled throw would otherwise
    /// take the whole process down with no explanation. Surface it and keep running:
    /// a failed command should never cost the user their session.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            MessageBox.Show(args.ExceptionObject?.ToString() ?? "unknown error",
                "LiveEditor — fatal", MessageBoxButton.OK, MessageBoxImage.Error);

        base.OnStartup(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.ToString(), "LiveEditor — unexpected error",
            MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
