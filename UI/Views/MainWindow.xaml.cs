using System.Windows;
using System.Windows.Controls;
using QB_TimeWarp.UI.ViewModels;

namespace QB_TimeWarp.UI.Views
{
    public partial class MainWindow : Window
    {
        private Button _activeNavButton;

        public MainWindow()
        {
            InitializeComponent();
            _activeNavButton = NavHome;
        }

        // ── Sidebar Navigation ─────────────────────────────────────────

        private void NavHome_Click(object sender, RoutedEventArgs e) => SwitchPage("Home");
        private void NavMigration_Click(object sender, RoutedEventArgs e) => SwitchPage("Migration");
        private void NavSettings_Click(object sender, RoutedEventArgs e) => SwitchPage("Settings");
        private void NavReports_Click(object sender, RoutedEventArgs e) => SwitchPage("Reports");
        private void NavLog_Click(object sender, RoutedEventArgs e) => SwitchPage("Log");

        private void NavAbout_Click(object sender, RoutedEventArgs e)
        {
            var aboutWindow = new AboutWindow();
            aboutWindow.Owner = this;
            aboutWindow.ShowDialog();
        }

        private void SwitchPage(string page)
        {
            // Hide all pages
            PageHome.Visibility = Visibility.Collapsed;
            PageMigration.Visibility = Visibility.Collapsed;
            PageSettings.Visibility = Visibility.Collapsed;
            PageReports.Visibility = Visibility.Collapsed;
            PageLog.Visibility = Visibility.Collapsed;

            // Reset all nav buttons to inactive style
            NavHome.Style = (Style)FindResource("NavButton");
            NavMigration.Style = (Style)FindResource("NavButton");
            NavSettings.Style = (Style)FindResource("NavButton");
            NavReports.Style = (Style)FindResource("NavButton");
            NavLog.Style = (Style)FindResource("NavButton");

            // Show selected page and activate nav button
            switch (page)
            {
                case "Home":
                    PageHome.Visibility = Visibility.Visible;
                    NavHome.Style = (Style)FindResource("NavButtonActive");
                    _activeNavButton = NavHome;
                    break;
                case "Migration":
                    PageMigration.Visibility = Visibility.Visible;
                    NavMigration.Style = (Style)FindResource("NavButtonActive");
                    _activeNavButton = NavMigration;
                    break;
                case "Settings":
                    PageSettings.Visibility = Visibility.Visible;
                    NavSettings.Style = (Style)FindResource("NavButtonActive");
                    _activeNavButton = NavSettings;
                    break;
                case "Reports":
                    PageReports.Visibility = Visibility.Visible;
                    NavReports.Style = (Style)FindResource("NavButtonActive");
                    _activeNavButton = NavReports;
                    break;
                case "Log":
                    PageLog.Visibility = Visibility.Visible;
                    NavLog.Style = (Style)FindResource("NavButtonActive");
                    _activeNavButton = NavLog;
                    break;
            }
        }

        // ── Drag-and-Drop ──────────────────────────────────────────────

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
                e.Effects = files.Any(f => f.EndsWith(".qbw", StringComparison.OrdinalIgnoreCase))
                    ? DragDropEffects.Copy
                    : DragDropEffects.None;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) && DataContext is MainViewModel vm)
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
                foreach (var file in files.Where(f => f.EndsWith(".qbw", StringComparison.OrdinalIgnoreCase)))
                {
                    if (vm.Files.All(f => f.FilePath != file))
                    {
                        vm.Files.Add(new QBWFileEntry { FilePath = file });
                        vm.AppendLog($"Added (drag-drop): {System.IO.Path.GetFileName(file)}");
                    }
                }
            }
        }

        // ── Auto-scroll log ───────────────────────────────────────────

        private void ActivityLogBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                tb.ScrollToEnd();
            }
        }
    }
}
