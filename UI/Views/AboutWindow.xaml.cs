using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace QB_TimeWarp.UI.Views
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
        }

        // ── Link handlers ──────────────────────────────────────────────

        private void OpenOSAWebsite(object sender, MouseButtonEventArgs e)
            => OpenUrl("https://oursystemadmin.com");

        private void OpenLRSWebsite(object sender, MouseButtonEventArgs e)
            => OpenUrl("https://liveremotesupport.net");

        private void OpenSupportEmail(object sender, MouseButtonEventArgs e)
            => OpenUrl("mailto:support@oursystemadmin.com");

        private void Close_Click(object sender, RoutedEventArgs e)
            => Close();

        // ── Helper ─────────────────────────────────────────────────────

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open link: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
