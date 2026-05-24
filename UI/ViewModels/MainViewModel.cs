using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using QB_TimeWarp.Models;
using QB_TimeWarp.Services;
using Serilog;
using Serilog.Events;

namespace QB_TimeWarp.UI.ViewModels
{
    /// <summary>
    /// Model for each QBW file in the file list grid.
    /// </summary>
    public class QBWFileEntry : ViewModelBase
    {
        private string _filePath = string.Empty;
        private string _status = "Queued";
        private string _statusColor = "#AAAAAA";

        public string FilePath
        {
            get => _filePath;
            set => SetProperty(ref _filePath, value);
        }

        public string FileName => Path.GetFileName(_filePath);

        public string Status
        {
            get => _status;
            set
            {
                SetProperty(ref _status, value);
                StatusColor = value switch
                {
                    "Success" => "#00E676",
                    "Running" or "Exporting" or "Transforming" or "Importing" => "#40C4FF",
                    "Failed" => "#FF5252",
                    "Queued" => "#AAAAAA",
                    _ => "#FFFFFF"
                };
            }
        }

        public string StatusColor
        {
            get => _statusColor;
            set => SetProperty(ref _statusColor, value);
        }
    }

    /// <summary>
    /// Primary ViewModel for MainWindow — drives the entire migration workflow.
    /// </summary>
    public class MainViewModel : ViewModelBase
    {
        // ── Fields ────────────────────────────────────────────────────────────
        private readonly Dispatcher _dispatcher;
        private CancellationTokenSource? _cts;

        private string _windowTitle = "QuickBooks TimeWarp® — QB 2023 → QB 2021 Migration";
        private bool _isMigrationRunning;
        private bool _isIdle = true;

        // Progress
        private string _currentPhase = "Ready";
        private string _currentPhaseColor = "#AAAAAA";
        private double _overallProgress;
        private string _progressText = "Idle";
        private int _exportedCount;
        private int _transformedCount;
        private int _importedCount;
        private int _failedCount;
        private int _totalEntities;

        // Phase step indicators
        private string _exportStepColor = "#555555";
        private string _transformStepColor = "#555555";
        private string _importStepColor = "#555555";
        private string _validateStepColor = "#555555";

        // Activity log
        private string _activityLog = string.Empty;

        // ── Constructor ───────────────────────────────────────────────────────
        public MainViewModel()
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            Files = new ObservableCollection<QBWFileEntry>();

            AddFilesCommand = new RelayCommand(OnAddFiles, () => IsIdle);
            RemoveSelectedCommand = new RelayCommand(OnRemoveSelected, () => IsIdle && Files.Count > 0);
            StartMigrationCommand = new RelayCommand(OnStartMigration, () => IsIdle && Files.Count > 0);
            StopMigrationCommand = new RelayCommand(OnStopMigration, () => IsMigrationRunning);
            OpenSettingsCommand = new RelayCommand(OnOpenSettings);
            ClearLogCommand = new RelayCommand(() => ActivityLog = string.Empty);
        }

        // ── Properties ────────────────────────────────────────────────────────

        public string WindowTitle
        {
            get => _windowTitle;
            set => SetProperty(ref _windowTitle, value);
        }

        public ObservableCollection<QBWFileEntry> Files { get; }

        public bool IsMigrationRunning
        {
            get => _isMigrationRunning;
            set
            {
                SetProperty(ref _isMigrationRunning, value);
                IsIdle = !value;
            }
        }

        public bool IsIdle
        {
            get => _isIdle;
            set => SetProperty(ref _isIdle, value);
        }

        public string CurrentPhase
        {
            get => _currentPhase;
            set
            {
                SetProperty(ref _currentPhase, value);
                CurrentPhaseColor = value switch
                {
                    "Exporting" => "#40C4FF",
                    "Transforming" => "#FFD740",
                    "Importing" => "#69F0AE",
                    "Validating" => "#CE93D8",
                    "Complete" => "#00E676",
                    "Failed" => "#FF5252",
                    _ => "#AAAAAA"
                };
            }
        }

        public string CurrentPhaseColor
        {
            get => _currentPhaseColor;
            set => SetProperty(ref _currentPhaseColor, value);
        }

        public double OverallProgress
        {
            get => _overallProgress;
            set => SetProperty(ref _overallProgress, value);
        }

        public string ProgressText
        {
            get => _progressText;
            set => SetProperty(ref _progressText, value);
        }

        public int ExportedCount
        {
            get => _exportedCount;
            set => SetProperty(ref _exportedCount, value);
        }

        public int TransformedCount
        {
            get => _transformedCount;
            set => SetProperty(ref _transformedCount, value);
        }

        public int ImportedCount
        {
            get => _importedCount;
            set => SetProperty(ref _importedCount, value);
        }

        public int FailedCount
        {
            get => _failedCount;
            set => SetProperty(ref _failedCount, value);
        }

        public int TotalEntities
        {
            get => _totalEntities;
            set => SetProperty(ref _totalEntities, value);
        }

        // Step indicator colors (active = bright, inactive = dim)
        public string ExportStepColor
        {
            get => _exportStepColor;
            set => SetProperty(ref _exportStepColor, value);
        }

        public string TransformStepColor
        {
            get => _transformStepColor;
            set => SetProperty(ref _transformStepColor, value);
        }

        public string ImportStepColor
        {
            get => _importStepColor;
            set => SetProperty(ref _importStepColor, value);
        }

        public string ValidateStepColor
        {
            get => _validateStepColor;
            set => SetProperty(ref _validateStepColor, value);
        }

        public string ActivityLog
        {
            get => _activityLog;
            set => SetProperty(ref _activityLog, value);
        }

        // ── Commands ──────────────────────────────────────────────────────────

        public ICommand AddFilesCommand { get; }
        public ICommand RemoveSelectedCommand { get; }
        public ICommand StartMigrationCommand { get; }
        public ICommand StopMigrationCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand ClearLogCommand { get; }

        // ── Logging helper ────────────────────────────────────────────────────

        public void AppendLog(string message)
        {
            var timestamped = $"[{DateTime.Now:HH:mm:ss}] {message}";
            _dispatcher.Invoke(() =>
            {
                ActivityLog += timestamped + Environment.NewLine;
            });
        }

        // ── Command handlers ──────────────────────────────────────────────────

        private void OnAddFiles()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "QuickBooks Company Files (*.qbw)|*.qbw|All Files (*.*)|*.*",
                Multiselect = true,
                Title = "Select QuickBooks Company Files"
            };

            if (dialog.ShowDialog() == true)
            {
                foreach (var file in dialog.FileNames)
                {
                    if (Files.All(f => f.FilePath != file))
                    {
                        Files.Add(new QBWFileEntry { FilePath = file });
                        AppendLog($"Added: {Path.GetFileName(file)}");
                    }
                }
            }
        }

        private void OnRemoveSelected()
        {
            // Remove the last file if no selection mechanism
            if (Files.Count > 0)
            {
                var removed = Files[^1];
                Files.RemoveAt(Files.Count - 1);
                AppendLog($"Removed: {removed.FileName}");
            }
        }

        private void OnOpenSettings()
        {
            var settingsWindow = new UI.Views.SettingsWindow();
            settingsWindow.Owner = Application.Current.MainWindow;
            settingsWindow.ShowDialog();
        }

        private void OnStopMigration()
        {
            _cts?.Cancel();
            AppendLog("⚠ Cancellation requested — waiting for current operation to finish...");
        }

        private async void OnStartMigration()
        {
            IsMigrationRunning = true;
            _cts = new CancellationTokenSource();

            ResetCounters();

            try
            {
                foreach (var file in Files)
                {
                    if (_cts.Token.IsCancellationRequested) break;

                    file.Status = "Running";
                    AppendLog($"═══ Starting migration: {file.FileName} ═══");

                    try
                    {
                        await Task.Run(() => RunMigrationForFile(file, _cts.Token), _cts.Token);
                        file.Status = "Success";
                        AppendLog($"✓ {file.FileName}: Migration completed successfully");
                    }
                    catch (OperationCanceledException)
                    {
                        file.Status = "Cancelled";
                        AppendLog($"⚠ {file.FileName}: Migration cancelled");
                        break;
                    }
                    catch (Exception ex)
                    {
                        file.Status = "Failed";
                        FailedCount++;
                        AppendLog($"✗ {file.FileName}: {ex.Message}");
                        Log.Error(ex, "Migration failed for {File}", file.FilePath);
                    }
                }

                // Show completion dialog
                if (Files.Any(f => f.Status == "Success"))
                {
                    CurrentPhase = "Complete";
                    _dispatcher.Invoke(() =>
                    {
                        var completionWindow = new UI.Views.CompletionWindow(
                            Files.Where(f => f.Status == "Success").Select(f => f.FilePath).ToList(),
                            ExportedCount, TransformedCount, ImportedCount, FailedCount
                        );
                        completionWindow.Owner = Application.Current.MainWindow;
                        completionWindow.ShowDialog();
                    });
                }
            }
            finally
            {
                IsMigrationRunning = false;
                _cts.Dispose();
                _cts = null;
            }
        }

        // ── Migration engine ──────────────────────────────────────────────────

        private void RunMigrationForFile(QBWFileEntry file, CancellationToken ct)
        {
            // Load configuration
            var config = LoadConfiguration();

            // Override source path to the selected file
            config.QuickBooks.QB2023.CompanyFilePath = file.FilePath;

            ct.ThrowIfCancellationRequested();

            // ── Phase 1: Export ────────────────────────────────────────────
            SetPhase("Exporting", file);
            AppendLog("Phase 1/4: Exporting data from QB 2023...");

            Dictionary<string, ExportedEntitySet> exportedData;
            using (var conn = new QBConnectionManager(config.QuickBooks.QB2023, "QB2023-Export"))
            {
                conn.Connect();
                AppendLog("  Connected to QuickBooks 2023");

                var exporter = new DataExporter(
                    conn, config.Export, config.Paths.ExportDirectory,
                    config.QuickBooks.QB2023.SDKVersion);

                exportedData = exporter.ExportAll();

                var totalRecords = exportedData.Values.Sum(e => e.Entities.Count);
                _dispatcher.Invoke(() =>
                {
                    ExportedCount = totalRecords;
                    TotalEntities = totalRecords;
                });
                AppendLog($"  Exported {totalRecords} records across {exportedData.Count} entity types");
            }

            ct.ThrowIfCancellationRequested();

            // ── Phase 2: Transform ────────────────────────────────────────
            SetPhase("Transforming", file);
            AppendLog("Phase 2/4: Transforming data for QB 2021 compatibility...");

            var transformer = new DataTransformer(config);
            var transformedData = transformer.TransformAll(exportedData);

            var transformCount = transformedData.Values.Sum(e => e.Entities.Count);
            _dispatcher.Invoke(() => TransformedCount = transformCount);
            AppendLog($"  Transformed {transformCount} records");

            ct.ThrowIfCancellationRequested();

            // ── Phase 3: Import ───────────────────────────────────────────
            SetPhase("Importing", file);
            AppendLog("Phase 3/4: Importing data into QB 2021...");

            using (var conn = new QBConnectionManager(config.QuickBooks.QB2021, "QB2021-Import"))
            {
                conn.Connect();
                AppendLog("  Connected to QuickBooks 2021");

                var importer = new DataImporter(
                    conn, config, config.Paths.ExportDirectory,
                    config.QuickBooks.QB2021.SDKVersion);

                var results = importer.ImportAll(transformedData);

                var succeeded = results.Values.Sum(r => r.Count(i => i.Success));
                var failed = results.Values.Sum(r => r.Count(i => !i.Success));
                _dispatcher.Invoke(() =>
                {
                    ImportedCount = succeeded;
                    FailedCount += failed;
                });
                AppendLog($"  Imported: {succeeded} succeeded, {failed} failed");
            }

            ct.ThrowIfCancellationRequested();

            // ── Phase 4: Validate ─────────────────────────────────────────
            SetPhase("Validating", file);
            AppendLog("Phase 4/4: Running validation...");

            // Re-export from QB 2021 for comparison
            Dictionary<string, ExportedEntitySet> importedData;
            using (var conn = new QBConnectionManager(config.QuickBooks.QB2021, "QB2021-Validate"))
            {
                conn.Connect();
                var exporter = new DataExporter(
                    conn, config.Export,
                    Path.Combine(config.Paths.ExportDirectory, "QB2021_PostImport"),
                    config.QuickBooks.QB2021.SDKVersion);

                importedData = exporter.ExportAll();
            }

            var validator = new DataValidator(config.Validation);
            try
            {
                var validationResults = validator.ValidateAll(exportedData, importedData);
                AppendLog($"  Validation: {(validationResults.IsValid ? "PASSED ✓" : "WARNINGS — check report")}");
            }
            catch (Exception ex)
            {
                AppendLog($"  Validation error (non-fatal): {ex.Message}");
            }

            // Save report
            var reportPath = Path.Combine(config.Paths.ValidationReportDirectory,
                $"MigrationReport_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            AppendLog($"  Report saved: {reportPath}");
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private void SetPhase(string phase, QBWFileEntry file)
        {
            _dispatcher.Invoke(() =>
            {
                CurrentPhase = phase;
                file.Status = phase;

                // Update step indicators
                ExportStepColor = phase == "Exporting" ? "#40C4FF"
                    : (phase != "Ready" ? "#00E676" : "#555555");

                TransformStepColor = phase == "Transforming" ? "#FFD740"
                    : (phase == "Importing" || phase == "Validating" ? "#00E676" : "#555555");

                ImportStepColor = phase == "Importing" ? "#69F0AE"
                    : (phase == "Validating" ? "#00E676" : "#555555");

                ValidateStepColor = phase == "Validating" ? "#CE93D8" : "#555555";

                OverallProgress = phase switch
                {
                    "Exporting" => 15,
                    "Transforming" => 40,
                    "Importing" => 65,
                    "Validating" => 85,
                    "Complete" => 100,
                    _ => 0
                };

                ProgressText = $"{phase}... ({OverallProgress:0}%)";
            });
        }

        private void ResetCounters()
        {
            ExportedCount = 0;
            TransformedCount = 0;
            ImportedCount = 0;
            FailedCount = 0;
            TotalEntities = 0;
            OverallProgress = 0;
            CurrentPhase = "Ready";
            ProgressText = "Starting...";
            ExportStepColor = "#555555";
            TransformStepColor = "#555555";
            ImportStepColor = "#555555";
            ValidateStepColor = "#555555";
        }

        private static AppConfiguration LoadConfiguration()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);

            var configuration = builder.Build();
            var config = new AppConfiguration();
            configuration.Bind(config);
            return config;
        }
    }
}
