using System.Diagnostics;
using System.IO;
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

        // ── Custom Title Bar Drag ──────────────────────────────────────

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        // ── Website / email link handlers ─────────────────────────────

        private void OpenOSAWebsite(object sender, MouseButtonEventArgs e)
            => OpenUrl("https://oursystemadmin.com");

        private void OpenLRSWebsite(object sender, MouseButtonEventArgs e)
            => OpenUrl("https://liveremotesupport.net");

        private void OpenSupportEmail(object sender, MouseButtonEventArgs e)
            => OpenUrl("mailto:support@oursystemadmin.com");

        // ── Legal document link handlers ──────────────────────────────

        private void OpenPrivacyPolicy(object sender, MouseButtonEventArgs e)
            => OpenLegalDocument("01-Privacy-Policy.md");

        private void OpenTermsOfService(object sender, MouseButtonEventArgs e)
            => OpenLegalDocument("03-Terms-of-Service-EULA.md");

        private void OpenDataSecurityPolicy(object sender, MouseButtonEventArgs e)
            => OpenLegalDocument("04-Data-Security-Handling-Policy.md");

        private void OpenSecurityGuarantee(object sender, MouseButtonEventArgs e)
            => OpenLegalDocument("SECURITY-GUARANTEE.md");

        private void Close_Click(object sender, RoutedEventArgs e)
            => Close();

        // ── Helpers ───────────────────────────────────────────────────

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

        private static void OpenLegalDocument(string filename)
        {
            try
            {
                var legalDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Legal");
                var filePath = Path.Combine(legalDir, filename);

                if (File.Exists(filePath))
                {
                    Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
                }
                else
                {
                    MessageBox.Show(
                        $"Document not found: {filename}\n\nExpected at: {filePath}",
                        "Document Not Found",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open document: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
