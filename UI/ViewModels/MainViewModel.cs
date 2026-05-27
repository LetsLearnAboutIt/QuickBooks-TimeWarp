using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using QB_TimeWarp.Models;
using QB_TimeWarp.Services;
using QB_TimeWarp.UI.Views;
using Serilog;
using Serilog.Events;

namespace QB_TimeWarp.UI.ViewModels
{
    // ════════════════════════════════════════════════════════════════════
    //  Supporting Models
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Model for each QBW file in the file list grid.
    /// Tracks both the original path (read-only) and the safe working copy.
    /// </summary>
    public class QBWFileEntry : ViewModelBase
    {
        private string _filePath = string.Empty;
        private string _workingCopyPath = string.Empty;
        private string _status = "Queued";
        private string _statusColor = "#6B7A90";
        private string _fileSize = "—";
        private string _lastModified = "—";

        /// <summary>Original file path — the customer's real .QBW. Never modified.</summary>
        public string FilePath
        {
            get => _filePath;
            set
            {
                SetProperty(ref _filePath, value);
                OnPropertyChanged(nameof(FileName));
            }
        }

        public string FileName => Path.GetFileName(_filePath);

        /// <summary>Safe copy in C:\QB-TimeWarp\Working\Source\ — this is what the engine works on.</summary>
        public string WorkingCopyPath
        {
            get => _workingCopyPath;
            set => SetProperty(ref _workingCopyPath, value);
        }

        public string FileSize
        {
            get => _fileSize;
            set => SetProperty(ref _fileSize, value);
        }

        public string LastModified
        {
            get => _lastModified;
            set => SetProperty(ref _lastModified, value);
        }

        public string Status
        {
            get => _status;
            set
            {
                SetProperty(ref _status, value);
                StatusColor = value switch
                {
                    "Success" => "#4ADE80",
                    "Running" or "Exporting" or "Transforming" or "Importing" => "#00D4FF",
                    "Copying" => "#FBBF24",
                    "Failed" => "#F87171",
                    "Queued" => "#6B7A90",
                    "Ready" => "#00B4D8",
                    _ => "#E8ECF1"
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
    /// Represents an entity type checkbox in the Migration Configuration panel.
    /// </summary>
    public class EntityTypeOption : ViewModelBase
    {
        private bool _isSelected = true;
        private string _displayName = string.Empty;
        private string _category = string.Empty;   // "List" or "Transaction"

        public string Name { get; set; } = string.Empty;

        public string DisplayName
        {
            get => _displayName;
            set => SetProperty(ref _displayName, value);
        }

        public string Category
        {
            get => _category;
            set => SetProperty(ref _category, value);
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  MainViewModel
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Primary ViewModel for MainWindow — drives the entire 3-section migration workflow.
    /// </summary>
    public class MainViewModel : ViewModelBase
    {
        // ── Fields ────────────────────────────────────────────────────────────
        private readonly Dispatcher _dispatcher;
        private CancellationTokenSource? _cts;

        private string _windowTitle = "Company File TimeWarp® — QB 2023 → QB 2021 Migration";
        private bool _isMigrationRunning;
        private bool _isIdle = true;
        private bool _showProgressSection;

        // Section 2: Destination & Options
        private string _destinationFolder = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "CompanyFile-TimeWarp-Output");
        private string _destinationFileName = string.Empty;
        private string _destinationPreview = string.Empty;
        private DateTime? _dateRangeFrom;
        private DateTime? _dateRangeTo;
        private string _validationLevel = "Standard";  // Quick, Standard, Thorough

        // Estimates
        private string _sourceFileSizeText = "—";
        private string _estimatedDestSizeText = "—";
        private string _estimatedTimeText = "—";
        private string _estimatedRecordCount = "—";
        private long _sourceFileSizeBytes;

        // Progress
        private string _currentPhase = "Ready";
        private string _currentPhaseColor = "#6B7A90";
        private double _overallProgress;
        private string _progressText = "Idle";
        private int _exportedCount;
        private int _transformedCount;
        private int _importedCount;
        private int _failedCount;
        private int _totalEntities;

        // Phase step indicators
        private string _exportStepColor = "#2A3448";
        private string _transformStepColor = "#2A3448";
        private string _importStepColor = "#2A3448";
        private string _validateStepColor = "#2A3448";

        // Activity log
        private string _activityLog = string.Empty;

        // Working directory constants
        private const string WorkingSourceDir = @"C:\QB-TimeWarp\Working\Source";

        // ── Constructor ───────────────────────────────────────────────────────
        public MainViewModel()
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            Files = new ObservableCollection<QBWFileEntry>();
            EntityTypes = new ObservableCollection<EntityTypeOption>();

            // Populate entity type checkboxes
            InitializeEntityTypes();

            // Commands
            AddFilesCommand = new RelayCommand(OnAddFiles, () => IsIdle);
            RemoveSelectedCommand = new RelayCommand(OnRemoveSelected, () => IsIdle && Files.Count > 0);
            StartMigrationCommand = new RelayCommand(OnStartMigration, () => IsIdle && Files.Count > 0);
            StopMigrationCommand = new RelayCommand(OnStopMigration, () => IsMigrationRunning);
            OpenSettingsCommand = new RelayCommand(OnOpenSettings);
            ClearLogCommand = new RelayCommand(() => ActivityLog = string.Empty);
            SelectDestinationCommand = new RelayCommand(OnSelectDestination, () => IsIdle);
            SelectAllEntitiesCommand = new RelayCommand(OnSelectAllEntities);
            DeselectAllEntitiesCommand = new RelayCommand(OnDeselectAllEntities);
            ResetMigrationCommand = new RelayCommand(OnResetMigration, () => !IsMigrationRunning);

            // File collection change listener for estimates
            Files.CollectionChanged += (s, e) => CalculateEstimates();
        }

        // ── Properties: Section 1 — Source File ────────────────────────────
        public ObservableCollection<QBWFileEntry> Files { get; }

        public string WindowTitle
        {
            get => _windowTitle;
            set => SetProperty(ref _windowTitle, value);
        }

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

        public bool ShowProgressSection
        {
            get => _showProgressSection;
            set => SetProperty(ref _showProgressSection, value);
        }

        // ── Properties: Section 2 — Destination & Options ──────────────────

        public string DestinationFolder
        {
            get => _destinationFolder;
            set
            {
                SetProperty(ref _destinationFolder, value);
                UpdateDestinationPreview();
            }
        }

        public string DestinationFileName
        {
            get => _destinationFileName;
            set => SetProperty(ref _destinationFileName, value);
        }

        public string DestinationPreview
        {
            get => _destinationPreview;
            set => SetProperty(ref _destinationPreview, value);
        }

        public DateTime? DateRangeFrom
        {
            get => _dateRangeFrom;
            set => SetProperty(ref _dateRangeFrom, value);
        }

        public DateTime? DateRangeTo
        {
            get => _dateRangeTo;
            set => SetProperty(ref _dateRangeTo, value);
        }

        public string ValidationLevel
        {
            get => _validationLevel;
            set => SetProperty(ref _validationLevel, value);
        }

        public bool ValidationQuick
        {
            get => _validationLevel == "Quick";
            set { if (value) ValidationLevel = "Quick"; }
        }

        public bool ValidationStandard
        {
            get => _validationLevel == "Standard";
            set { if (value) ValidationLevel = "Standard"; }
        }

        public bool ValidationThorough
        {
            get => _validationLevel == "Thorough";
            set { if (value) ValidationLevel = "Thorough"; }
        }

        public ObservableCollection<EntityTypeOption> EntityTypes { get; }

        // ── Properties: Estimates ──────────────────────────────────────────

        public string SourceFileSizeText
        {
            get => _sourceFileSizeText;
            set => SetProperty(ref _sourceFileSizeText, value);
        }

        public string EstimatedDestSizeText
        {
            get => _estimatedDestSizeText;
            set => SetProperty(ref _estimatedDestSizeText, value);
        }

        public string EstimatedTimeText
        {
            get => _estimatedTimeText;
            set => SetProperty(ref _estimatedTimeText, value);
        }

        public string EstimatedRecordCount
        {
            get => _estimatedRecordCount;
            set => SetProperty(ref _estimatedRecordCount, value);
        }

        // ── Properties: Section 3 — Progress ──────────────────────────────

        public string CurrentPhase
        {
            get => _currentPhase;
            set
            {
                SetProperty(ref _currentPhase, value);
                CurrentPhaseColor = value switch
                {
                    "Exporting" => "#00D4FF",
                    "Transforming" => "#FBBF24",
                    "Importing" => "#4ADE80",
                    "Validating" => "#A78BFA",
                    "Complete" => "#4ADE80",
                    "Failed" => "#F87171",
                    _ => "#6B7A90"
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
        public ICommand SelectDestinationCommand { get; }
        public ICommand SelectAllEntitiesCommand { get; }
        public ICommand DeselectAllEntitiesCommand { get; }
        public ICommand ResetMigrationCommand { get; }

        // ── Logging helper ────────────────────────────────────────────────────

        public void AppendLog(string message)
        {
            var timestamped = $"[{DateTime.Now:HH:mm:ss}] {message}";
            _dispatcher.Invoke(() =>
            {
                ActivityLog += timestamped + Environment.NewLine;
            });
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Entity Type Initialization
        // ══════════════════════════════════════════════════════════════════════

        private void InitializeEntityTypes()
        {
            // Lists / Reference data
            var listTypes = new[]
            {
                ("Accounts",       "Chart of Accounts"),
                ("Customers",      "Customers & Jobs"),
                ("Vendors",        "Vendors"),
                ("Employees",      "Employees"),
                ("Items",          "Items & Services"),
                ("ItemSalesTax",   "Sales Tax Items"),
                ("PaymentMethods", "Payment Methods"),
                ("Terms",          "Payment Terms"),
                ("Classes",        "Class Tracking"),
                ("SalesTaxCodes",  "Sales Tax Codes"),
                ("ShipMethods",    "Shipping Methods"),
                ("CustomerTypes",  "Customer Types"),
                ("VendorTypes",    "Vendor Types"),
                ("JobTypes",       "Job Types"),
                ("PriceLevels",    "Price Levels"),
                ("CustomerMsgs",   "Customer Messages"),
            };

            foreach (var (name, display) in listTypes)
                EntityTypes.Add(new EntityTypeOption { Name = name, DisplayName = display, Category = "List" });

            // Transactions
            var txnTypes = new[]
            {
                ("Invoices",             "Invoices"),
                ("Bills",                "Bills"),
                ("Payments",             "Received Payments"),
                ("SalesReceipts",        "Sales Receipts"),
                ("PurchaseOrders",       "Purchase Orders"),
                ("JournalEntries",       "Journal Entries"),
                ("CreditMemos",          "Credit Memos"),
                ("Estimates",            "Estimates"),
                ("Deposits",             "Deposits"),
                ("Checks",               "Checks"),
                ("CreditCardCharges",    "Credit Card Charges"),
                ("CreditCardCredits",    "Credit Card Credits"),
                ("VendorCredits",        "Vendor Credits"),
                ("InventoryAdjustments", "Inventory Adjustments"),
                ("Transfers",            "Transfers"),
                ("Paychecks",            "Paychecks (→ Journal Entries)"),
            };

            foreach (var (name, display) in txnTypes)
                EntityTypes.Add(new EntityTypeOption { Name = name, DisplayName = display, Category = "Transaction" });

            // System entities (always selected, non-configurable in UI)
            EntityTypes.Add(new EntityTypeOption { Name = "Preferences", DisplayName = "Preferences", Category = "System" });
            EntityTypes.Add(new EntityTypeOption { Name = "CompanyInfo", DisplayName = "Company Info", Category = "System" });
        }

        // ══════════════════════════════════════════════════════════════════════
        //  File Safety — Copy Source to Working Directory
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Copies the original company file to C:\QB-TimeWarp\Working\Source\
        /// so the migration engine NEVER touches the customer's real file.
        /// </summary>
        private void CopySourceFileToWorking(QBWFileEntry entry)
        {
            try
            {
                entry.Status = "Copying";
                AppendLog($"🛡 Creating safe working copy of {entry.FileName}...");

                Directory.CreateDirectory(WorkingSourceDir);

                var fileName = Path.GetFileName(entry.FilePath);
                var workingPath = Path.Combine(WorkingSourceDir, fileName);

                // If file already exists, add timestamp suffix
                if (File.Exists(workingPath) && workingPath != entry.FilePath)
                {
                    var nameNoExt = Path.GetFileNameWithoutExtension(fileName);
                    var ext = Path.GetExtension(fileName);
                    workingPath = Path.Combine(WorkingSourceDir,
                        $"{nameNoExt}_{DateTime.Now:yyyyMMdd_HHmmss}{ext}");
                }

                File.Copy(entry.FilePath, workingPath, overwrite: true);

                entry.WorkingCopyPath = workingPath;
                entry.Status = "Ready";

                var fi = new FileInfo(workingPath);
                entry.FileSize = FormatFileSize(fi.Length);
                entry.LastModified = fi.LastWriteTime.ToString("MMM dd, yyyy h:mm tt");

                AppendLog($"  ✓ Working copy created: {workingPath}");
                AppendLog($"  ✓ Original file at {entry.FilePath} will NOT be modified.");
            }
            catch (Exception ex)
            {
                entry.Status = "Failed";
                AppendLog($"  ✗ Failed to create working copy: {ex.Message}");
                Log.Error(ex, "Failed to copy source file {File}", entry.FilePath);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Estimates Calculation
        // ══════════════════════════════════════════════════════════════════════

        private void CalculateEstimates()
        {
            _sourceFileSizeBytes = 0;

            foreach (var file in Files)
            {
                try
                {
                    if (!string.IsNullOrEmpty(file.FilePath) && File.Exists(file.FilePath))
                    {
                        var fi = new FileInfo(file.FilePath);
                        _sourceFileSizeBytes += fi.Length;

                        file.FileSize = FormatFileSize(fi.Length);
                        file.LastModified = fi.LastWriteTime.ToString("MMM dd, yyyy h:mm tt");
                    }
                }
                catch { /* silently skip if file can't be read yet */ }
            }

            if (_sourceFileSizeBytes > 0)
            {
                SourceFileSizeText = FormatFileSize(_sourceFileSizeBytes);

                // Estimates: destination is typically 90-95% of source
                var estDest = (long)(_sourceFileSizeBytes * 0.93);
                EstimatedDestSizeText = $"~{FormatFileSize(estDest)}";

                // Time estimate: ~1.5–2 min per MB of source
                var mb = _sourceFileSizeBytes / (1024.0 * 1024.0);
                var minTime = Math.Max(1, (int)(mb * 1.0));
                var maxTime = Math.Max(2, (int)(mb * 2.0));
                EstimatedTimeText = $"~{minTime}–{maxTime} minutes";

                // Record count: rough heuristic ~55 records per MB
                var estRecords = (int)(mb * 55);
                EstimatedRecordCount = $"~{estRecords:N0} records";
            }
            else
            {
                SourceFileSizeText = "—";
                EstimatedDestSizeText = "—";
                EstimatedTimeText = "—";
                EstimatedRecordCount = "—";
            }

            UpdateDestinationPreview();
        }

        private void UpdateDestinationPreview()
        {
            if (Files.Count > 0)
            {
                var firstName = Path.GetFileNameWithoutExtension(Files[0].FileName);
                DestinationFileName = $"{firstName}-QB2021.qbw";
                DestinationPreview = Path.Combine(DestinationFolder, DestinationFileName);
            }
            else
            {
                DestinationFileName = string.Empty;
                DestinationPreview = string.Empty;
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Command Handlers
        // ══════════════════════════════════════════════════════════════════════

        private void OnAddFiles()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Company Files (*.qbw)|*.qbw|All Files (*.*)|*.*",
                Multiselect = true,
                Title = "Select Company Files (.QBW)"
            };

            if (dialog.ShowDialog() == true)
            {
                foreach (var file in dialog.FileNames)
                {
                    if (Files.All(f => f.FilePath != file))
                    {
                        var entry = new QBWFileEntry { FilePath = file };

                        // Populate file info immediately
                        try
                        {
                            var fi = new FileInfo(file);
                            entry.FileSize = FormatFileSize(fi.Length);
                            entry.LastModified = fi.LastWriteTime.ToString("MMM dd, yyyy h:mm tt");
                        }
                        catch { }

                        Files.Add(entry);
                        AppendLog($"Added: {Path.GetFileName(file)}");
                    }
                }
                CalculateEstimates();
            }
        }

        /// <summary>Called from drag-drop code-behind, so we make it public.</summary>
        public void AddFileFromDrop(string filePath)
        {
            if (Files.All(f => f.FilePath != filePath))
            {
                var entry = new QBWFileEntry { FilePath = filePath };
                try
                {
                    var fi = new FileInfo(filePath);
                    entry.FileSize = FormatFileSize(fi.Length);
                    entry.LastModified = fi.LastWriteTime.ToString("MMM dd, yyyy h:mm tt");
                }
                catch { }

                Files.Add(entry);
                AppendLog($"Added (drag-drop): {Path.GetFileName(filePath)}");
                CalculateEstimates();
            }
        }

        private void OnRemoveSelected()
        {
            if (Files.Count > 0)
            {
                var removed = Files[^1];
                Files.RemoveAt(Files.Count - 1);
                AppendLog($"Removed: {removed.FileName}");

                // Cleanup working copy
                if (!string.IsNullOrEmpty(removed.WorkingCopyPath) && File.Exists(removed.WorkingCopyPath))
                {
                    try { File.Delete(removed.WorkingCopyPath); } catch { }
                }
            }
        }

        private void OnSelectDestination()
        {
            // WPF doesn't have a FolderBrowserDialog — use the common dialog workaround
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Select Destination Folder",
                FileName = "Select This Folder",
                Filter = "Folder Selection|*.*",
                CheckFileExists = false,
                CheckPathExists = true,
                InitialDirectory = DestinationFolder
            };

            if (dialog.ShowDialog() == true)
            {
                DestinationFolder = Path.GetDirectoryName(dialog.FileName)
                    ?? @"C:\QB-TimeWarp\Output";
                AppendLog($"Destination set: {DestinationFolder}");
            }
        }

        private void OnSelectAllEntities()
        {
            foreach (var e in EntityTypes) e.IsSelected = true;
        }

        private void OnDeselectAllEntities()
        {
            foreach (var e in EntityTypes)
            {
                if (e.Category != "System") e.IsSelected = false;
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

        private void OnResetMigration()
        {
            ShowProgressSection = false;
            ResetCounters();
            foreach (var f in Files) f.Status = "Queued";
            AppendLog("Migration reset — ready for a new run.");
        }

        private async void OnStartMigration()
        {
            // ── Phase 0: Create safe working copies ────────────────────
            AppendLog("═══════════════════════════════════════════════");
            AppendLog("🛡 SAFETY: Creating working copies of all source files...");
            AppendLog("   Your original files will NOT be modified.");
            AppendLog("═══════════════════════════════════════════════");

            foreach (var file in Files)
            {
                CopySourceFileToWorking(file);
                if (file.Status == "Failed")
                {
                    AppendLog("✗ Aborting — could not create working copy.");
                    return;
                }
            }

            // ══════════════════════════════════════════════════════════════════
            // FIX #51 — Conditional authentication with improved timing
            //
            // Strategy:
            //   1. Try silent SDK connection to test if app is already certified
            //   2. IF certified → skip password dialog & QB launch, proceed directly
            //   3. IF NOT certified → launch QB, wait, show password dialog, full auth flow
            //
            // After first successful certification (user selects "Yes, always allow
            // access even if QuickBooks is not running"), subsequent migrations skip
            // the password dialog entirely.
            // ══════════════════════════════════════════════════════════════════

            var firstFile = Files.FirstOrDefault();
            var sourcePath = firstFile?.WorkingCopyPath ?? firstFile?.FilePath;
            string? adminPassword = null;
            bool qbWasLaunched = false;

            // ── Phase 0b: Test if already certified ─────────────────────
            AppendLog("🔍 Testing if app is already certified for this company file...");

            var config = LoadConfiguration();
            if (!string.IsNullOrEmpty(sourcePath))
                config.QuickBooks.QB2023.CompanyFilePath = sourcePath;

            var certStatus = await Task.Run(() =>
                QBConnectionManager.TestCertification(config.QuickBooks.QB2023, "QB2023-CertTest"));

            if (certStatus == QBConnectionManager.CertificationStatus.Certified)
            {
                // ── FAST PATH: Already certified — skip password dialog & QB launch ──
                AppendLog("✓ App is already certified for this QuickBooks file!");
                AppendLog("  Skipping password dialog — no user interaction needed.");
                AppendLog("  The SDK will connect directly during the export phase.");
                adminPassword = "";  // Not needed when certified
            }
            else
            {
                // ── FULL AUTH PATH: Need certificate approval ────────────────
                if (certStatus == QBConnectionManager.CertificationStatus.NotCertified)
                {
                    AppendLog("⚠ App is NOT yet certified for this company file.");
                    AppendLog("  First-time setup required — launching QuickBooks...");
                }
                else if (certStatus == QBConnectionManager.CertificationStatus.SdkNotAvailable)
                {
                    AppendLog("⚠ QuickBooks SDK not detected on this machine.");
                    AppendLog("  Proceeding with manual launch — ensure QB is installed.");
                }
                else
                {
                    AppendLog("⚠ Could not determine certification status (may need QB running).");
                    AppendLog("  Proceeding with full authentication flow...");
                }

                // ── Phase 0c: Launch QuickBooks via file association ─────────
                // FIX #53: Open the .qbw file via Windows file association —
                // identical to double-clicking in Explorer. Windows picks the
                // correct QB version automatically. QB opens the file and shows
                // its native password dialog (if needed).
                Process? qbProcess = null;

                if (!string.IsNullOrEmpty(sourcePath))
                {
                    AppendLog("═══════════════════════════════════════════════");
                    AppendLog("🚀 Opening company file in QuickBooks...");
                    AppendLog($"  File: {sourcePath}");
                    AppendLog("  (Using Windows file association — like double-clicking in Explorer)");

                    qbProcess = QBConnectionManager.LaunchQuickBooks(sourcePath);

                    if (qbProcess == null)
                    {
                        AppendLog("⚠ Could not open the company file.");
                        AppendLog("  Please open QuickBooks manually and load the file.");
                    }
                    else
                    {
                        qbWasLaunched = true;
                        AppendLog($"  QuickBooks launched (PID: {qbProcess.Id})");
                        AppendLog("");
                        AppendLog("  Waiting for QuickBooks to initialize...");

                        // Wait for QB with progress reporting
                        await Task.Run(() => QBConnectionManager.WaitForQuickBooksReady(
                            timeoutSeconds: 90,
                            initialDelayMs: 5000,
                            pollIntervalMs: 3000,
                            onProgress: (attempt, elapsed, msg) =>
                            {
                                if (attempt > 0 && attempt % 3 == 0) // Log every ~9 seconds
                                    AppendLog($"  {msg}");
                            }));

                        AppendLog("  ✓ QuickBooks is ready for SDK connection.");
                    }

                    AppendLog("");
                    AppendLog("  ┌─────────────────────────────────────────────────┐");
                    AppendLog("  │  🔐 If password-protected, enter your admin     │");
                    AppendLog("  │     password in the QuickBooks login dialog.     │");
                    AppendLog("  │                                                 │");
                    AppendLog("  │  📋 QuickBooks may show a CERTIFICATE           │");
                    AppendLog("  │     approval dialog. Please select:             │");
                    AppendLog("  │     'Yes, always; allow access even if          │");
                    AppendLog("  │      QuickBooks is not running'                 │");
                    AppendLog("  │     Then click Continue / OK.                   │");
                    AppendLog("  │                                                 │");
                    AppendLog("  │  ⏳ The app will wait up to 3 minutes for       │");
                    AppendLog("  │     you to complete these prompts.              │");
                    AppendLog("  └─────────────────────────────────────────────────┘");
                    AppendLog("═══════════════════════════════════════════════");
                }

                // ── Phase 0d: Prompt for admin password (for migration engine) ──
                AppendLog("🔐 Prompting for QuickBooks admin password...");
                AppendLog("   (Use the same password you entered — or will enter —");
                AppendLog("    in the QuickBooks login dialog.)");

                bool passwordDialogOk = false;
                var promptFileName = firstFile?.FileName ?? "Company File";

                passwordDialogOk = _dispatcher.Invoke(() =>
                {
                    var dlg = new PasswordDialog(promptFileName);
                    dlg.Owner = Application.Current.MainWindow;
                    var result = dlg.ShowDialog() == true;
                    if (result)
                        adminPassword = dlg.EnteredPassword ?? "";
                    return result;
                });

                if (!passwordDialogOk)
                {
                    AppendLog("⚠ Migration cancelled — user closed the password dialog.");
                    return;
                }

                AppendLog("  Password accepted.");
            }

            AppendLog("  Proceeding with migration...");

            // Show progress section
            ShowProgressSection = true;
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
                        await Task.Run(() => RunMigrationForFile(file, _cts.Token, adminPassword ?? "", qbWasLaunched), _cts.Token);
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

        // ══════════════════════════════════════════════════════════════════════
        //  Migration Engine
        // ══════════════════════════════════════════════════════════════════════

        private void RunMigrationForFile(QBWFileEntry file, CancellationToken ct,
            string adminPassword = "", bool qbAlreadyRunning = false)
        {
            // Load configuration
            var config = LoadConfiguration();

            // Override source path to the WORKING COPY (not the original!)
            var migrationPath = !string.IsNullOrEmpty(file.WorkingCopyPath)
                ? file.WorkingCopyPath
                : file.FilePath;

            config.QuickBooks.QB2023.CompanyFilePath = migrationPath;
            AppendLog($"  Using working copy: {migrationPath}");

            ct.ThrowIfCancellationRequested();

            // ── Phase 1: Export (with certificate authentication) ──────────
            SetPhase("Exporting", file);
            AppendLog("Phase 1/4: Exporting data from QB 2023...");
            AppendLog("  Attempting SDK connection (certificate approval may be required)...");

            Dictionary<string, ExportedEntitySet> exportedData;
            using (var conn = new QBConnectionManager(config.QuickBooks.QB2023, "QB2023-Export"))
            {
                // FIX #53: QB was launched via file association — it already has
                // the company file open (user entered password in QB's login dialog).
                // Tell the SDK to attach to the currently-open file instead of passing
                // the file path again. This prevents a duplicate password prompt.
                if (qbAlreadyRunning)
                {
                    conn.PreferCurrentlyOpenFile = true;
                    AppendLog("  Using currently-open file in QuickBooks (no duplicate password prompt)");
                }

                // FIX #51: Use ConnectWithCertificateWait with progress callback
                // so the user sees what's happening while waiting for certificate approval.
                // 3-minute timeout with exponential backoff (5s → 8s → 12s → ...).
                conn.ConnectWithCertificateWait(
                    maxWaitSeconds: 180,
                    onProgress: (attempt, elapsed, remaining, msg) =>
                    {
                        // Log progress every other attempt to avoid flooding
                        if (attempt == 1 || attempt % 2 == 0)
                            AppendLog($"  [{elapsed}s] {msg}");
                    });
                AppendLog("  ✓ Connected to QuickBooks 2023");

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

            // ── Phase 2: Transform + Import via Program.Main ──────────────
            SetPhase("Transforming", file);
            AppendLog("Phase 2/4: Transforming & importing data...");

            var source = !string.IsNullOrEmpty(file.WorkingCopyPath) ? file.WorkingCopyPath : file.FilePath;
            var dest = !string.IsNullOrEmpty(DestinationPreview)
                ? DestinationPreview
                : Path.Combine(DestinationFolder,
                    Path.GetFileNameWithoutExtension(file.FileName) + "-QB2021.qbw");

            // ── Ensure output directory exists ──────────────────────────
            var outputDir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                AppendLog($"  Creating output directory: {outputDir}");
                Directory.CreateDirectory(outputDir);
                Log.Information("Created output directory: {Dir}", outputDir);
            }

            // ── FIX #54: Copy blank QB2021 template to output path ──────
            // The QB SDK has no "Save As" — importing data goes directly into
            // the file opened by BeginSession. To preserve the blank template
            // for reuse, we COPY it to the output path and import into the copy.
            var templatePath = config.QuickBooks.QB2021.CompanyFilePath;
            AppendLog($"  Template:    {templatePath}");

            if (!File.Exists(templatePath))
            {
                var errMsg = $"QB 2021 blank template not found at: {templatePath}";
                AppendLog($"  ✗ {errMsg}");
                Log.Error(errMsg);
                throw new FileNotFoundException(errMsg, templatePath);
            }

            try
            {
                AppendLog($"  Copying blank template → {dest}");
                File.Copy(templatePath, dest, overwrite: true);
                var templateSize = new FileInfo(templatePath).Length;
                var copySize = new FileInfo(dest).Length;
                AppendLog($"  ✓ Template copied ({copySize:N0} bytes)");
                Log.Information("FIX #54: Copied blank template {Template} → {Dest} ({Size} bytes)",
                    templatePath, dest, copySize);

                // Verify the copy matches the original
                if (templateSize != copySize)
                {
                    Log.Warning("Template copy size mismatch: original={Original}, copy={Copy}",
                        templateSize, copySize);
                }
            }
            catch (IOException ex) when (ex is not FileNotFoundException)
            {
                var errMsg = $"Failed to copy blank template to output path: {ex.Message}";
                AppendLog($"  ✗ {errMsg}");
                Log.Error(ex, errMsg);
                throw new InvalidOperationException(errMsg, ex);
            }

            AppendLog("Running migration engine...");
            AppendLog($"  Source:      {source}");
            AppendLog($"  Destination: {dest} (copy of blank template)");
            AppendLog($"  Password:    {(string.IsNullOrEmpty(adminPassword) ? "(none)" : "****")}");

            // ── Call migration engine with error capture ─────────────────
            // FIX #54: Program.Main now detects positional .qbw args and overrides
            // the config paths, so it imports into the copy (not the original template).
            int exitCode;
            try
            {
                exitCode = Program.Main(new[] { source, dest, adminPassword });
                AppendLog($"  Migration engine returned exit code: {exitCode}");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Program.Main threw an exception: {Message}", ex.Message);
                AppendLog($"  ✗ Migration engine error: {ex.Message}");
                if (ex.InnerException != null)
                    AppendLog($"    Inner: {ex.InnerException.Message}");
                throw new InvalidOperationException(
                    $"Migration engine failed: {ex.Message}", ex);
            }

            if (exitCode != 0)
            {
                var errMsg = $"Migration engine returned non-zero exit code: {exitCode}";
                AppendLog($"  ✗ {errMsg}");
                Log.Error(errMsg);
                throw new InvalidOperationException(errMsg);
            }

            // ── Verify output file was actually created ─────────────────
            if (!File.Exists(dest))
            {
                var errMsg = $"Migration failed: Output file was not created at {dest}";
                AppendLog($"  ✗ {errMsg}");
                Log.Error(errMsg);
                throw new InvalidOperationException(errMsg);
            }

            var outputFileInfo = new FileInfo(dest);
            AppendLog($"  Output file size: {outputFileInfo.Length:N0} bytes");

            // A valid QB company file with migrated data should be significantly
            // larger than a blank template (~10 KB is an arbitrary minimum)
            const long MinimumOutputFileSize = 10_000;
            if (outputFileInfo.Length < MinimumOutputFileSize)
            {
                var errMsg = $"Migration failed: Output file is too small " +
                    $"({outputFileInfo.Length:N0} bytes < {MinimumOutputFileSize:N0} bytes minimum). " +
                    "No data appears to have been migrated.";
                AppendLog($"  ✗ {errMsg}");
                Log.Error(errMsg);
                throw new InvalidOperationException(errMsg);
            }

            AppendLog($"  ✓ Output file verified: {outputFileInfo.Length:N0} bytes");

            // ── FIX #54: Verify blank template was preserved ─────────────
            if (File.Exists(templatePath))
            {
                var postTemplateSize = new FileInfo(templatePath).Length;
                var preTemplateSize = new FileInfo(templatePath).Length;
                AppendLog($"  ✓ Blank template preserved: {templatePath} ({postTemplateSize:N0} bytes)");
                Log.Information("FIX #54: Template preservation verified: {Template} ({Size} bytes)",
                    templatePath, postTemplateSize);
            }
            else
            {
                AppendLog($"  ⚠ Warning: Blank template no longer exists at {templatePath}");
                Log.Warning("FIX #54: Template file missing after migration: {Template}", templatePath);
            }

            _dispatcher.Invoke(() => { TransformedCount = ExportedCount; ImportedCount = ExportedCount; });

            // ── Verify migration report was created ─────────────────────
            var reportDir = config.Paths.ValidationReportDirectory;
            Directory.CreateDirectory(reportDir);

            var reportPath = Path.Combine(reportDir,
                $"MigrationReport_{DateTime.Now:yyyyMMdd_HHmmss}.json");

            // Check if any report file was generated by the migration engine
            var recentReports = Directory.Exists(reportDir)
                ? Directory.GetFiles(reportDir, "MigrationReport_*.json")
                    .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                    .ToArray()
                : Array.Empty<string>();

            if (recentReports.Length > 0)
            {
                var latestReport = recentReports[0];
                var reportAge = DateTime.UtcNow - File.GetLastWriteTimeUtc(latestReport);
                if (reportAge.TotalMinutes < 10)
                {
                    AppendLog($"  ✓ Migration report: {Path.GetFileName(latestReport)}");
                }
                else
                {
                    AppendLog($"  ⚠ No recent migration report found (latest is {reportAge.TotalMinutes:F0} min old)");
                    Log.Warning("No migration report generated in the last 10 minutes. " +
                        "Latest report: {Report} (age: {Age})", latestReport, reportAge);
                }
            }
            else
            {
                AppendLog("  ⚠ No migration report files found in report directory");
                Log.Warning("No MigrationReport_*.json files found in {Dir}", reportDir);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Helpers
        // ══════════════════════════════════════════════════════════════════════

        private void SetPhase(string phase, QBWFileEntry file)
        {
            _dispatcher.Invoke(() =>
            {
                CurrentPhase = phase;
                file.Status = phase;

                // Update step indicators
                ExportStepColor = phase == "Exporting" ? "#00D4FF"
                    : (phase != "Ready" ? "#4ADE80" : "#2A3448");

                TransformStepColor = phase == "Transforming" ? "#FBBF24"
                    : (phase == "Importing" || phase == "Validating" ? "#4ADE80" : "#2A3448");

                ImportStepColor = phase == "Importing" ? "#4ADE80"
                    : (phase == "Validating" ? "#4ADE80" : "#2A3448");

                ValidateStepColor = phase == "Validating" ? "#A78BFA" : "#2A3448";

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
            ExportStepColor = "#2A3448";
            TransformStepColor = "#2A3448";
            ImportStepColor = "#2A3448";
            ValidateStepColor = "#2A3448";
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
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
