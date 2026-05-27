using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace QB_TimeWarp.UI.Views
{
    /// <summary>
    /// Represents a single output file row in the completion dialog.
    /// </summary>
    public class OutputFileItem
    {
        public string Icon { get; set; } = "📄";
        public string Label { get; set; } = string.Empty;
        public string SubLabel { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public bool Exists => File.Exists(FilePath);
    }

    public partial class CompletionWindow : Window
    {
        private readonly List<string> _sourceFiles;
        private readonly string _downloadsFolder;

        private readonly string? _outputFilePath;

        public CompletionWindow(
            List<string> successfulFiles,
            int exported, int transformed, int imported, int failed,
            string? outputFilePath = null)
        {
            InitializeComponent();

            _sourceFiles = successfulFiles;
            _outputFilePath = outputFilePath;
            _downloadsFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

            // Set stats
            ExportedLabel.Text = exported.ToString("N0");
            TransformedLabel.Text = transformed.ToString("N0");
            ImportedLabel.Text = imported.ToString("N0");
            FailedLabel.Text = failed.ToString("N0");

            // File name
            if (successfulFiles.Count == 1)
                FileNameLabel.Text = Path.GetFileName(successfulFiles[0]);
            else
                FileNameLabel.Text = $"{successfulFiles.Count} files processed";

            SaveLocationLabel.Text = $"Save your files to: {_downloadsFolder}";

            // Build output file list
            var outputFiles = new List<OutputFileItem>();

            // Converted QBW file (the output)
            // FIX #55: Use the actual output file path passed from RunMigrationForFile,
            // not the template location from appsettings.json. The template is the
            // BLANK source; the output is where the migrated data was written.
            string targetQBW;
            if (!string.IsNullOrEmpty(_outputFilePath) && File.Exists(_outputFilePath))
            {
                targetQBW = _outputFilePath;
            }
            else
            {
                // Fallback: read template path from config (for backward compatibility)
                var configPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
                targetQBW = @"C:\QB-TimeWarp\Working\QB21_Blank_Template\Blank_Template.qbw";
                try
                {
                    if (File.Exists(configPath))
                    {
                        var json = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(configPath));
                        targetQBW = json.SelectToken("QuickBooks.QB2021.CompanyFilePath")?.ToString() ?? targetQBW;
                    }
                }
                catch { }
            }

            outputFiles.Add(new OutputFileItem
            {
                Icon = "📦",
                Label = "Converted QuickBooks file (.QBW)",
                SubLabel = Path.GetFileName(targetQBW),
                FilePath = targetQBW
            });

            // Validation report
            var validationDir = Path.Combine(Directory.GetCurrentDirectory(), "Validation");
            var latestReport = Directory.Exists(validationDir)
                ? Directory.GetFiles(validationDir, "MigrationReport_*.json")
                    .OrderByDescending(f => f).FirstOrDefault()
                : null;
            outputFiles.Add(new OutputFileItem
            {
                Icon = "📊",
                Label = "Validation Report — JSON",
                SubLabel = latestReport != null ? Path.GetFileName(latestReport) : "(not generated)",
                FilePath = latestReport ?? ""
            });

            // Log file
            var logDir = Path.Combine(Directory.GetCurrentDirectory(), "Logs");
            var latestLog = Directory.Exists(logDir)
                ? Directory.GetFiles(logDir, "QB-TimeWarp-*.log")
                    .OrderByDescending(f => f).FirstOrDefault()
                : null;
            outputFiles.Add(new OutputFileItem
            {
                Icon = "📝",
                Label = "Migration Log",
                SubLabel = latestLog != null ? Path.GetFileName(latestLog) : "(not generated)",
                FilePath = latestLog ?? ""
            });

            // Export data
            var exportDir = Path.Combine(Directory.GetCurrentDirectory(), "ExportedData");
            outputFiles.Add(new OutputFileItem
            {
                Icon = "💾",
                Label = "Exported Data (JSON)",
                SubLabel = Directory.Exists(exportDir) ? $"{Directory.GetFiles(exportDir, "*.json").Length} files" : "(not generated)",
                FilePath = exportDir
            });

            OutputFilesList.ItemsSource = outputFiles;
        }

        private void CopyToDownloads_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string filePath)
            {
                CopyFileToDownloads(filePath);
            }
        }

        private void ShowInFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string filePath)
            {
                try
                {
                    if (File.Exists(filePath))
                        Process.Start("explorer.exe", $"/select,\"{filePath}\"");
                    else if (Directory.Exists(filePath))
                        Process.Start("explorer.exe", filePath);
                    else
                        MessageBox.Show("File not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void CopyAllToDownloads_Click(object sender, RoutedEventArgs e)
        {
            int copied = 0;
            foreach (OutputFileItem item in OutputFilesList.Items)
            {
                if (CopyFileToDownloads(item.FilePath))
                    copied++;
            }
            MessageBox.Show($"Copied {copied} item(s) to Downloads.", "Copy Complete",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private bool CopyFileToDownloads(string sourcePath)
        {
            try
            {
                if (string.IsNullOrEmpty(sourcePath)) return false;

                Directory.CreateDirectory(_downloadsFolder);

                if (File.Exists(sourcePath))
                {
                    var dest = Path.Combine(_downloadsFolder, Path.GetFileName(sourcePath));
                    File.Copy(sourcePath, dest, overwrite: true);
                    return true;
                }
                else if (Directory.Exists(sourcePath))
                {
                    var destDir = Path.Combine(_downloadsFolder, Path.GetFileName(sourcePath));
                    Directory.CreateDirectory(destDir);
                    foreach (var file in Directory.GetFiles(sourcePath))
                    {
                        File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error copying: {ex.Message}", "Copy Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return false;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }
    }
}
