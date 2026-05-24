using System.Windows;

namespace QB_TimeWarp.UI
{
    /// <summary>
    /// WPF Application entry point for QB-TimeWarp GUI mode.
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Set up global exception handling for WPF
            DispatcherUnhandledException += (s, args) =>
            {
                MessageBox.Show(
                    $"An unexpected error occurred:\n\n{args.Exception.Message}\n\nCheck the log file for details.",
                    "QB-TimeWarp Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                args.Handled = true;
            };
        }
    }
}
