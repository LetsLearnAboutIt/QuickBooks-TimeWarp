using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
            StateChanged += MainWindow_StateChanged;
        }

        // ── Custom Title Bar — Drag, Double-click Maximize ──────────

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
            }
            else
            {
                DragMove();
            }
        }

        private void TitleBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Intentionally empty — completes the mouse event pair
        }

        // ── Window Control Buttons ──────────────────────────────────

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            ToggleMaximize();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            // Update the maximize/restore glyph
            if (BtnMaximize != null)
            {
                // Segoe MDL2 Assets: E922 = Maximize, E923 = Restore
                BtnMaximize.Content = WindowState == WindowState.Maximized
                    ? "\uE923"
                    : "\uE922";
                BtnMaximize.ToolTip = WindowState == WindowState.Maximized
                    ? "Restore Down"
                    : "Maximize";
            }
        }

        // ── Sidebar Navigation ─────────────────────────────────────────

        private void NavHome_Click(object sender, RoutedEventArgs e) => SwitchPage("Home");
        private void NavSource_Click(object sender, RoutedEventArgs e) => SwitchPage("Source");
        private void NavDestination_Click(object sender, RoutedEventArgs e) => SwitchPage("Destination");
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
            PageSource.Visibility = Visibility.Collapsed;
            PageDestination.Visibility = Visibility.Collapsed;
            PageMigration.Visibility = Visibility.Collapsed;
            PageSettings.Visibility = Visibility.Collapsed;
            PageReports.Visibility = Visibility.Collapsed;
            PageLog.Visibility = Visibility.Collapsed;

            // Reset all nav buttons to inactive style
            NavHome.Style = (Style)FindResource("NavButton");
            NavSource.Style = (Style)FindResource("NavButton");
            NavDestination.Style = (Style)FindResource("NavButton");
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
                case "Source":
                    PageSource.Visibility = Visibility.Visible;
                    NavSource.Style = (Style)FindResource("NavButtonActive");
                    _activeNavButton = NavSource;
                    break;
                case "Destination":
                    PageDestination.Visibility = Visibility.Visible;
                    NavDestination.Style = (Style)FindResource("NavButtonActive");
                    _activeNavButton = NavDestination;
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
                    vm.AddFileFromDrop(file);
                }
            }
        }

        // ── Navigate between pages from buttons ──────────────────────

        private void GoToSource_Click(object sender, RoutedEventArgs e) => SwitchPage("Source");
        private void GoToDestination_Click(object sender, RoutedEventArgs e) => SwitchPage("Destination");
        private void GoToMigration_Click(object sender, RoutedEventArgs e) => SwitchPage("Migration");

        // ── Auto-scroll log boxes ────────────────────────────────────

        private void ActivityLogBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox tb)
                tb.ScrollToEnd();
        }

        private void MigrationLogBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox tb)
                tb.ScrollToEnd();
        }
    }
}
