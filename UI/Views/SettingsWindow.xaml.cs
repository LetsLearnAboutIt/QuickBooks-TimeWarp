using System.IO;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace QB_TimeWarp.UI.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly string _settingsPath;

        public SettingsWindow()
        {
            InitializeComponent();
            _settingsPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
            LoadCurrentSettings();
        }

        private void LoadCurrentSettings()
        {
            try
            {
                if (!File.Exists(_settingsPath)) return;

                var json = File.ReadAllText(_settingsPath);
                var root = JObject.Parse(json);

                // Connection tab
                QB2023Path.Text = root.SelectToken("QuickBooks.QB2023.CompanyFilePath")?.ToString() ?? "";
                QB2023SDKVersion.Text = root.SelectToken("QuickBooks.QB2023.SDKVersion")?.ToString() ?? "16.0";
                QB2023Timeout.Text = root.SelectToken("QuickBooks.QB2023.TimeoutSeconds")?.ToString() ?? "300";
                QB2021Path.Text = root.SelectToken("QuickBooks.QB2021.CompanyFilePath")?.ToString() ?? "";
                QB2021SDKVersion.Text = root.SelectToken("QuickBooks.QB2021.SDKVersion")?.ToString() ?? "15.0";
                MaxRetries.Text = root.SelectToken("QuickBooks.QB2021.MaxRetries")?.ToString() ?? "3";

                // Export/Import tab
                DateFrom.Text = root.SelectToken("Export.DateRangeStart")?.ToString() ?? "";
                DateTo.Text = root.SelectToken("Export.DateRangeEnd")?.ToString() ?? "";
                SkipOnError.IsChecked = root.SelectToken("Import.SkipOnError")?.Value<bool>() ?? true;
                UseStagedImport.IsChecked = root.SelectToken("Import.UseStagedImport")?.Value<bool>() ?? true;
                AutoCreateDeps.IsChecked = root.SelectToken("Import.AutoCreateMissingDependencies")?.Value<bool>() ?? true;
                DryRunMode.IsChecked = root.SelectToken("Import.DryRun")?.Value<bool>() ?? false;

                // Transformation
                ReactivateInactive.IsChecked = root.SelectToken("TransformationRules.ReactivateInactiveEntities")?.Value<bool>() ?? true;
                PreserveClasses.IsChecked = root.SelectToken("TransformationRules.PreserveClassTracking")?.Value<bool>() ?? true;
                MatchAccounting.IsChecked = root.SelectToken("TransformationRules.MatchAccountingModel")?.Value<bool>() ?? true;

                // Paths tab
                ExportDir.Text = root.SelectToken("Paths.ExportDirectory")?.ToString() ?? ".\\ExportedData";
                SchemaDir.Text = root.SelectToken("Paths.SchemaDirectory")?.ToString() ?? ".\\Schemas";
                LogDir.Text = root.SelectToken("Paths.LogDirectory")?.ToString() ?? ".\\Logs";
                ValidationDir.Text = root.SelectToken("Paths.ValidationReportDirectory")?.ToString() ?? ".\\Validation";
                SourceWorkingDir.Text = root.SelectToken("WorkingDirectories.SourcePath")?.ToString() ?? @"C:\QB-TimeWarp\Working\Source";
                TargetWorkingDir.Text = root.SelectToken("WorkingDirectories.TargetPath")?.ToString() ?? @"C:\QB-TimeWarp\Working\Target";

                // Logging tab
                var logLevel = root.SelectToken("Logging.MinimumLevel")?.ToString() ?? "Information";
                LogLevel.SelectedIndex = logLevel switch
                {
                    "Debug" => 0,
                    "Information" => 1,
                    "Warning" => 2,
                    "Error" => 3,
                    _ => 1
                };
                EnableConsoleLog.IsChecked = root.SelectToken("Logging.EnableConsoleOutput")?.Value<bool>() ?? true;
                EnableFileLog.IsChecked = root.SelectToken("Logging.EnableFileOutput")?.Value<bool>() ?? true;
                MaxLogSize.Text = root.SelectToken("Logging.MaxFileSizeMB")?.ToString() ?? "100";
                RetainedFiles.Text = root.SelectToken("Logging.RetainedFileCount")?.ToString() ?? "10";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading settings: {ex.Message}", "Settings Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                JObject root;
                if (File.Exists(_settingsPath))
                {
                    root = JObject.Parse(File.ReadAllText(_settingsPath));
                }
                else
                {
                    root = new JObject();
                }

                // Update values (create paths if missing)
                SetToken(root, "QuickBooks.QB2023.CompanyFilePath", QB2023Path.Text);
                SetToken(root, "QuickBooks.QB2023.SDKVersion", QB2023SDKVersion.Text);
                SetToken(root, "QuickBooks.QB2023.TimeoutSeconds", int.Parse(QB2023Timeout.Text));
                SetToken(root, "QuickBooks.QB2021.CompanyFilePath", QB2021Path.Text);
                SetToken(root, "QuickBooks.QB2021.SDKVersion", QB2021SDKVersion.Text);
                SetToken(root, "QuickBooks.QB2021.MaxRetries", int.Parse(MaxRetries.Text));

                SetToken(root, "Export.DateRangeStart", string.IsNullOrEmpty(DateFrom.Text) ? null : DateFrom.Text);
                SetToken(root, "Export.DateRangeEnd", string.IsNullOrEmpty(DateTo.Text) ? null : DateTo.Text);
                SetToken(root, "Import.SkipOnError", SkipOnError.IsChecked ?? true);
                SetToken(root, "Import.UseStagedImport", UseStagedImport.IsChecked ?? true);
                SetToken(root, "Import.AutoCreateMissingDependencies", AutoCreateDeps.IsChecked ?? true);
                SetToken(root, "Import.DryRun", DryRunMode.IsChecked ?? false);

                SetToken(root, "TransformationRules.ReactivateInactiveEntities", ReactivateInactive.IsChecked ?? true);
                SetToken(root, "TransformationRules.PreserveClassTracking", PreserveClasses.IsChecked ?? true);
                SetToken(root, "TransformationRules.MatchAccountingModel", MatchAccounting.IsChecked ?? true);

                SetToken(root, "Paths.ExportDirectory", ExportDir.Text);
                SetToken(root, "Paths.SchemaDirectory", SchemaDir.Text);
                SetToken(root, "Paths.LogDirectory", LogDir.Text);
                SetToken(root, "Paths.ValidationReportDirectory", ValidationDir.Text);
                SetToken(root, "WorkingDirectories.SourcePath", SourceWorkingDir.Text);
                SetToken(root, "WorkingDirectories.TargetPath", TargetWorkingDir.Text);

                var logLevelStr = ((System.Windows.Controls.ComboBoxItem)LogLevel.SelectedItem).Content.ToString();
                SetToken(root, "Logging.MinimumLevel", logLevelStr);
                SetToken(root, "Logging.EnableConsoleOutput", EnableConsoleLog.IsChecked ?? true);
                SetToken(root, "Logging.EnableFileOutput", EnableFileLog.IsChecked ?? true);
                SetToken(root, "Logging.MaxFileSizeMB", int.Parse(MaxLogSize.Text));
                SetToken(root, "Logging.RetainedFileCount", int.Parse(RetainedFiles.Text));

                File.WriteAllText(_settingsPath, root.ToString(Formatting.Indented));

                MessageBox.Show("Settings saved successfully.", "Settings",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving settings: {ex.Message}", "Settings Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

        private void BrowseQB2023_Click(object sender, RoutedEventArgs e)
        {
            var path = BrowseForQBW();
            if (path != null) QB2023Path.Text = path;
        }

        private void BrowseQB2021_Click(object sender, RoutedEventArgs e)
        {
            var path = BrowseForQBW();
            if (path != null) QB2021Path.Text = path;
        }

        private string? BrowseForQBW()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "QuickBooks Company Files (*.qbw)|*.qbw|All Files (*.*)|*.*",
                Title = "Select QuickBooks Company File"
            };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        /// <summary>
        /// Sets a nested JToken value by dotted path, creating intermediate objects as needed.
        /// </summary>
        private static void SetToken(JObject root, string dottedPath, object? value)
        {
            var parts = dottedPath.Split('.');
            JObject current = root;

            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (current[parts[i]] is not JObject child)
                {
                    child = new JObject();
                    current[parts[i]] = child;
                }
                current = child;
            }

            current[parts[^1]] = value != null ? JToken.FromObject(value) : JValue.CreateNull();
        }
    }
}
