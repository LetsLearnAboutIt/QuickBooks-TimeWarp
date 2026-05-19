using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using QB_TimeWarp.Helpers;
using QB_TimeWarp.Models;
using QB_TimeWarp.Services;
using Serilog;
using Serilog.Events;

namespace QB_TimeWarp
{
    /// <summary>
    /// QB-TimeWarp: QuickBooks 2023 → 2021 Data Migration Tool
    /// 
    /// This program migrates all data from a QuickBooks Desktop 2023 company file
    /// to a QuickBooks Desktop 2021 company file via the QBXML SDK.
    /// 
    /// Prerequisites:
    ///   1. QuickBooks Desktop 2023 and 2021 installed on this machine
    ///   2. QuickBooks SDK (QBXMLRP2.RequestProcessor COM object) registered
    ///   3. Both company files accessible and not locked by other users
    ///   4. This application authorized in both QB instances
    ///      (QB > Edit > Preferences > Integrated Applications > Company Preferences)
    ///   5. appsettings.json configured with correct company file paths
    ///   6. FieldMappings.json reviewed and customized for your data
    /// 
    /// Execution flow:
    ///   Step 1: Extract schema from QB 2021 (document target capabilities)
    ///   Step 2: Export all data from QB 2023
    ///   Step 3: Transform data using field mappings
    ///   Step 4: Import transformed data into QB 2021
    ///   Step 5: Validate imported data
    ///   Step 6: Generate final migration report
    /// </summary>
    class Program
    {
        private static AppConfiguration _config = null!;
        private static WorkingDirectoryManager? _workingDirManager;

        static int Main(string[] args)
        {
            try
            {
                // Show banner
                ConsoleBanner.ShowHeader();

                // Load configuration
                _config = LoadConfiguration();

                // Initialize logging
                InitializeLogging(_config.Logging, _config.Paths.LogDirectory);

                Log.Information("QB-TimeWarp starting...");

                // Parse command-line arguments for mode selection
                var mode = ParseMode(args);
                bool forceRefresh = args.Any(a => a.Equals("--refresh", StringComparison.OrdinalIgnoreCase));

                // Handle --cleanup command
                if (args.Any(a => a.Equals("--cleanup", StringComparison.OrdinalIgnoreCase)))
                {
                    return RunCleanup();
                }

                // ─── CRITICAL: Initialize Working Copies BEFORE any QB operations ───
                if (_config.WorkingDirectories.AutoCreateWorkingCopies &&
                    !string.IsNullOrEmpty(_config.SourceFiles.DesktopFolder) &&
                    !string.IsNullOrEmpty(_config.TargetFiles.DesktopFolder))
                {
                    InitializeWorkingCopies(forceRefresh);
                }
                else
                {
                    Log.Information("Working copy auto-creation disabled or source/target folders not configured.");
                    Log.Information("Using configured CompanyFilePath values directly.");
                    ValidateCompanyFilePathsAreNotDesktopOriginals();
                }

                Log.Information("╔══════════════════════════════════════════════════════════════╗");
                Log.Information("║  WORKING WITH COPIES — ORIGINALS PRESERVED                  ║");
                Log.Information("╠══════════════════════════════════════════════════════════════╣");
                Log.Information("║  Source (QB 2023): {Source}", _config.QuickBooks.QB2023.CompanyFilePath);
                Log.Information("║  Target (QB 2021): {Target}", _config.QuickBooks.QB2021.CompanyFilePath);
                Log.Information("╚══════════════════════════════════════════════════════════════╝");

                // Execute the selected mode
                var exitCode = mode switch
                {
                    RunMode.Full => RunFullMigration(),
                    RunMode.ExportOnly => RunExportOnly(),
                    RunMode.ImportOnly => RunImportOnly(),
                    RunMode.ValidateOnly => RunValidateOnly(),
                    RunMode.SchemaOnly => RunSchemaOnly(),
                    _ => RunFullMigration()
                };

                Log.Information("QB-TimeWarp finished with exit code: {ExitCode}", exitCode);
                return exitCode;
            }
            catch (OriginalFileProtectionException ex)
            {
                Log.Fatal("🛑 SAFETY VIOLATION: {Message}", ex.Message);
                ConsoleBanner.ShowError("SAFETY VIOLATION — OPERATION HALTED");
                ConsoleBanner.ShowError(ex.Message);
                ConsoleBanner.ShowError("Original files are PROTECTED. Check your configuration.");
                return 99; // Special exit code for safety violations
            }
            catch (WorkingCopyException ex)
            {
                Log.Fatal("Working copy error: {Message}", ex.Message);
                ConsoleBanner.ShowError($"Working copy error: {ex.Message}");
                ConsoleBanner.ShowError("Cannot proceed without valid working copies.");
                return 2;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Unhandled exception in QB-TimeWarp: {Message}", ex.Message);
                ConsoleBanner.ShowError($"Fatal error: {ex.Message}");
                ConsoleBanner.ShowError("See log file for details.");
                return 1;
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        /// <summary>
        /// Initializes working copies of QB company files from Desktop originals.
        /// Updates _config.QuickBooks paths to point to working copies.
        /// This MUST run before any QuickBooks operations.
        /// </summary>
        private static void InitializeWorkingCopies(bool forceRefresh)
        {
            ConsoleBanner.ShowStep(0, 0, "Initialize Working Copies (SAFETY FIRST)");

            _workingDirManager = new WorkingDirectoryManager(
                _config.SourceFiles,
                _config.TargetFiles,
                _config.WorkingDirectories);

            bool success = _workingDirManager.InitializeWorkingCopies(forceRefresh);

            if (!success)
                throw new WorkingCopyException("Working copy initialization returned false.");

            // CRITICAL: Update QB config to point to WORKING copies, not originals
            _config.QuickBooks.QB2023.CompanyFilePath = _workingDirManager.WorkingSourceFilePath!;
            _config.QuickBooks.QB2021.CompanyFilePath = _workingDirManager.WorkingTargetFilePath!;

            Log.Information("QB 2023 CompanyFilePath updated to working copy: {Path}",
                _config.QuickBooks.QB2023.CompanyFilePath);
            Log.Information("QB 2021 CompanyFilePath updated to working copy: {Path}",
                _config.QuickBooks.QB2021.CompanyFilePath);

            // Show working copy summary
            var summary = _workingDirManager.GetWorkingSummary();
            ConsoleBanner.ShowSummary("WORKING COPY STATUS", summary);
        }

        /// <summary>
        /// Safety check: If working copies are not being auto-created,
        /// ensure the configured paths are NOT pointing to Desktop originals.
        /// </summary>
        private static void ValidateCompanyFilePathsAreNotDesktopOriginals()
        {
            var sourcePath = _config.QuickBooks.QB2023.CompanyFilePath.ToUpperInvariant();
            var targetPath = _config.QuickBooks.QB2021.CompanyFilePath.ToUpperInvariant();

            if (sourcePath.Contains("\\DESKTOP\\") && !sourcePath.Contains("\\WORKING\\"))
            {
                throw new OriginalFileProtectionException(
                    $"QB 2023 CompanyFilePath points to a Desktop folder: {_config.QuickBooks.QB2023.CompanyFilePath}. " +
                    "Enable AutoCreateWorkingCopies or change the path to a Working directory.");
            }

            if (targetPath.Contains("\\DESKTOP\\") && !targetPath.Contains("\\WORKING\\"))
            {
                throw new OriginalFileProtectionException(
                    $"QB 2021 CompanyFilePath points to a Desktop folder: {_config.QuickBooks.QB2021.CompanyFilePath}. " +
                    "Enable AutoCreateWorkingCopies or change the path to a Working directory.");
            }
        }

        /// <summary>
        /// Handles the --cleanup command to remove working directories.
        /// </summary>
        private static int RunCleanup()
        {
            Log.Information("Running cleanup of working directories...");
            ConsoleBanner.ShowStep(1, 1, "Cleanup Working Directories");

            var manager = new WorkingDirectoryManager(
                _config.SourceFiles,
                _config.TargetFiles,
                _config.WorkingDirectories);

            manager.CleanupWorkingDirectories();
            ConsoleBanner.ShowSuccess("Working directories cleaned up. Originals remain untouched.");
            return 0;
        }

        /// <summary>
        /// Runs the complete migration pipeline: Schema → Export → Transform → Import → Validate.
        /// </summary>
        private static int RunFullMigration()
        {
            const int totalSteps = 6;
            var overallStart = DateTime.UtcNow;

            // ─── Step 1: Extract QB 2021 Schema ────────────────────────
            ConsoleBanner.ShowStep(1, totalSteps, "Extract QB 2021 Schema");
            QBSchemaExport? schema = null;
            try
            {
                using var qb2021SchemaConn = new QBConnectionManager(
                    _config.QuickBooks.QB2021, "QB2021-Schema");
                qb2021SchemaConn.Connect();

                var schemaExtractor = new SchemaExtractor(
                    qb2021SchemaConn,
                    _config.QuickBooks.QB2021.SDKVersion,
                    _config.Paths.SchemaDirectory);

                schema = schemaExtractor.ExtractAllSchemas();
                ConsoleBanner.ShowSuccess($"Schema extracted: {schema.EntitySchemas.Count} entity types");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Schema extraction failed (non-fatal): {Message}", ex.Message);
                ConsoleBanner.ShowWarning($"Schema extraction skipped: {ex.Message}");
                ConsoleBanner.ShowWarning("Using built-in schema definitions.");
            }

            // ─── Step 2: Export Data from QB 2023 ──────────────────────
            ConsoleBanner.ShowStep(2, totalSteps, "Export Data from QB 2023");
            Dictionary<string, ExportedEntitySet> exportedData;

            using (var qb2023Conn = new QBConnectionManager(
                _config.QuickBooks.QB2023, "QB2023-Export"))
            {
                qb2023Conn.Connect();
                ConsoleBanner.ShowSuccess("Connected to QuickBooks 2023");

                var exporter = new DataExporter(
                    qb2023Conn,
                    _config.Export,
                    _config.Paths.ExportDirectory,
                    _config.QuickBooks.QB2023.SDKVersion);

                exportedData = exporter.ExportAll();
                ConsoleBanner.ShowSuccess($"Exported {exportedData.Values.Sum(e => e.TotalCount)} records across {exportedData.Count} entity types");
            }

            // ─── Step 3: Transform Data ────────────────────────────────
            ConsoleBanner.ShowStep(3, totalSteps, "Transform Data (QB 2023 → QB 2021 format)");
            Dictionary<string, ExportedEntitySet> transformedData;

            var transformer = new DataTransformer(_config.Paths.FieldMappingsFile, _config.TransformationRules);
            transformedData = transformer.TransformAll(exportedData);

            var transformationReport = transformer.GetTransformationReport();
            if (transformationReport.TotalReactivatedEntities > 0)
                ConsoleBanner.ShowSuccess($"Reactivated {transformationReport.TotalReactivatedEntities} inactive entities");
            if (transformationReport.ClassTracking != null && transformationReport.ClassTracking.TotalClassesInSource > 0)
                ConsoleBanner.ShowSuccess($"Preserved {transformationReport.ClassTracking.TotalClassesInSource} class assignments");
            if (!string.IsNullOrEmpty(transformationReport.SourceAccountingMethod))
                ConsoleBanner.ShowSuccess($"Accounting method: {transformationReport.SourceAccountingMethod}");

            ConsoleBanner.ShowSuccess("Data transformation complete");

            // Save transformed data
            SaveTransformedData(transformedData);

            // ─── Step 3.5: Pre-Import Journal Validation ───────────────
            if (_config.Validation.EnableJournalValidation)
            {
                ConsoleBanner.ShowStep(3, totalSteps, "Pre-Import Journal Validation");
                try
                {
                    using var preValidConn1 = new QBConnectionManager(_config.QuickBooks.QB2023, "QB2023-PreValid");
                    using var preValidConn2 = new QBConnectionManager(_config.QuickBooks.QB2021, "QB2021-PreValid");
                    preValidConn1.Connect();
                    preValidConn2.Connect();

                    var preValidator = new DataValidator(
                        preValidConn1, preValidConn2,
                        _config.Validation,
                        _config.Paths.ValidationReportDirectory);

                    var preImportJournalReport = preValidator.ValidatePreImport(transformedData);

                    if (preImportJournalReport.IsBalanced)
                    {
                        ConsoleBanner.ShowSuccess("Pre-import journal validation PASSED - all entries balanced");
                    }
                    else
                    {
                        ConsoleBanner.ShowWarning($"Pre-import journal validation found {preImportJournalReport.UnbalancedJournalEntries} unbalanced journal entries");
                        ConsoleBanner.ShowWarning("Review the journal integrity report before proceeding with import.");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Pre-import journal validation failed (non-fatal): {Message}", ex.Message);
                    ConsoleBanner.ShowWarning($"Pre-import journal validation skipped: {ex.Message}");
                }
            }

            // ─── Step 4: Import Data into QB 2021 ──────────────────────
            ConsoleBanner.ShowStep(4, totalSteps, "Import Data into QB 2021");
            MigrationReport report;

            using (var qb2021Conn = new QBConnectionManager(
                _config.QuickBooks.QB2021, "QB2021-Import"))
            {
                qb2021Conn.Connect();
                ConsoleBanner.ShowSuccess("Connected to QuickBooks 2021");

                var importer = new DataImporter(
                    qb2021Conn,
                    _config.Import,
                    _config.QuickBooks.QB2021.SDKVersion,
                    _config.TransformationRules);

                // Step 4a: Apply accounting preferences before import
                if (_config.TransformationRules.MatchAccountingModel)
                {
                    ConsoleBanner.ShowStep(4, totalSteps, "Applying Accounting Preferences");
                    try
                    {
                        // Query source preferences
                        using var qb2023PrefsConn = new QBConnectionManager(
                            _config.QuickBooks.QB2023, "QB2023-Prefs");
                        qb2023PrefsConn.Connect();
                        var sourcePrefsImporter = new DataImporter(
                            qb2023PrefsConn, _config.Import,
                            _config.QuickBooks.QB2023.SDKVersion,
                            _config.TransformationRules);
                        var sourcePrefs = sourcePrefsImporter.QueryCompanyPreferences();
                        report = new MigrationReport { StartTime = overallStart };
                        report.SourcePreferences = sourcePrefs;

                        // Apply to target
                        var applied = importer.ApplyAccountingPreferences(sourcePrefs);
                        if (applied)
                            ConsoleBanner.ShowSuccess($"Accounting method set to: {sourcePrefs.AccountingMethod}");
                        else
                            ConsoleBanner.ShowWarning("Accounting preferences may need manual verification");

                        // Query target preferences to verify
                        var targetPrefs = importer.QueryCompanyPreferences();
                        report.TargetPreferences = targetPrefs;
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Could not apply accounting preferences: {Message}", ex.Message);
                        ConsoleBanner.ShowWarning($"Accounting preferences: {ex.Message}");
                    }
                }

                // Step 4b: Ensure classes exist before importing transactions
                if (_config.TransformationRules.PreserveClassTracking)
                {
                    var discoveredClasses = transformer.GetDiscoveredClasses();
                    if (discoveredClasses.Any())
                    {
                        ConsoleBanner.ShowStep(4, totalSteps, "Ensuring Classes Exist in QB 2021");
                        var classSummary = importer.EnsureClassesExist(discoveredClasses);
                        if (transformationReport.ClassTracking != null)
                        {
                            transformationReport.ClassTracking.ClassesCreatedInTarget = classSummary.ClassesCreatedInTarget;
                            transformationReport.ClassTracking.ClassesAlreadyExisting = classSummary.ClassesAlreadyExisting;
                            transformationReport.ClassTracking.TotalClassesInTarget = classSummary.TotalClassesInTarget;
                            transformationReport.ClassTracking.CreatedClasses = classSummary.CreatedClasses;
                            transformationReport.ClassTracking.MissingClasses = classSummary.MissingClasses;
                        }
                        if (classSummary.ClassesCreatedInTarget > 0)
                            ConsoleBanner.ShowSuccess($"Created {classSummary.ClassesCreatedInTarget} classes in QB 2021");
                    }
                }

                // Step 4c: Import all data (staged or flat)
                if (_config.Import.UseStagedImport)
                {
                    ConsoleBanner.ShowStep(4, totalSteps, "Import Data (STAGED MODE)");
                    var stagedResult = importer.ImportDataInStages(transformedData);
                    report = importer.ConvertToMigrationReport(stagedResult);
                }
                else
                {
                    report = importer.ImportAll(transformedData);
                }
                report.TransformationReport = transformationReport;

                if (report.TotalRecordsFailed > 0)
                    ConsoleBanner.ShowWarning($"Import completed with {report.TotalRecordsFailed} failures");
                else
                    ConsoleBanner.ShowSuccess($"All {report.TotalRecordsSucceeded} records imported successfully");
            }

            // ─── Step 5: Validate Imported Data ────────────────────────
            ConsoleBanner.ShowStep(5, totalSteps, "Validate Imported Data");
            ValidationReport? validationReport = null;

            try
            {
                // Re-export data from QB 2021 for comparison
                Dictionary<string, ExportedEntitySet> reimportedData;

                using (var qb2021ValidConn = new QBConnectionManager(
                    _config.QuickBooks.QB2021, "QB2021-Validate"))
                {
                    qb2021ValidConn.Connect();

                    var reExporter = new DataExporter(
                        qb2021ValidConn,
                        _config.Export,
                        Path.Combine(_config.Paths.ExportDirectory, "QB2021_PostImport"),
                        _config.QuickBooks.QB2021.SDKVersion);

                    reimportedData = reExporter.ExportAll();
                }

                // Run validation
                using var qb2023ValidConn = new QBConnectionManager(
                    _config.QuickBooks.QB2023, "QB2023-Validate");
                using var qb2021ValidConn2 = new QBConnectionManager(
                    _config.QuickBooks.QB2021, "QB2021-Validate2");

                qb2023ValidConn.Connect();
                qb2021ValidConn2.Connect();

                var validator = new DataValidator(
                    qb2023ValidConn, qb2021ValidConn2,
                    _config.Validation,
                    _config.Paths.ValidationReportDirectory);

                validationReport = validator.ValidateAll(exportedData, reimportedData);
                report.ValidationReport = validationReport;

                if (validationReport.IsValid)
                    ConsoleBanner.ShowSuccess("Validation PASSED");
                else
                    ConsoleBanner.ShowWarning($"Validation found {validationReport.TotalDiscrepancies} discrepancies");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Validation failed: {Message}", ex.Message);
                ConsoleBanner.ShowWarning($"Validation skipped: {ex.Message}");
            }

            // ─── Step 6: Generate Final Report ─────────────────────────
            ConsoleBanner.ShowStep(6, totalSteps, "Generate Migration Report");
            report.EndTime = DateTime.UtcNow;
            SaveMigrationReport(report);

            // Show final summary
            ShowFinalSummary(report, overallStart);

            return report.OverallStatus == MigrationStatus.Failed ? 1 : 0;
        }

        /// <summary>
        /// Export-only mode: exports data from QB 2023 without importing.
        /// </summary>
        private static int RunExportOnly()
        {
            ConsoleBanner.ShowStep(1, 1, "Export Data from QB 2023");

            using var qb2023Conn = new QBConnectionManager(
                _config.QuickBooks.QB2023, "QB2023-Export");
            qb2023Conn.Connect();

            var exporter = new DataExporter(
                qb2023Conn,
                _config.Export,
                _config.Paths.ExportDirectory,
                _config.QuickBooks.QB2023.SDKVersion);

            var exportedData = exporter.ExportAll();
            ConsoleBanner.ShowSuccess($"Exported {exportedData.Values.Sum(e => e.TotalCount)} records");

            return 0;
        }

        /// <summary>
        /// Import-only mode: imports previously exported & transformed data into QB 2021.
        /// </summary>
        private static int RunImportOnly()
        {
            ConsoleBanner.ShowStep(1, 2, "Load Previously Exported Data");
            var transformedDir = Path.Combine(_config.Paths.ExportDirectory, "Transformed");
            if (!Directory.Exists(transformedDir))
                transformedDir = _config.Paths.ExportDirectory;

            var data = DataExporter.LoadExportedData(transformedDir);
            ConsoleBanner.ShowSuccess($"Loaded {data.Values.Sum(e => e.TotalCount)} records");

            ConsoleBanner.ShowStep(2, 2, "Import Data into QB 2021");
            using var qb2021Conn = new QBConnectionManager(
                _config.QuickBooks.QB2021, "QB2021-Import");
            qb2021Conn.Connect();

            var importer = new DataImporter(
                qb2021Conn, _config.Import, _config.QuickBooks.QB2021.SDKVersion);

            var report = importer.ImportAll(data);
            SaveMigrationReport(report);

            return report.OverallStatus == MigrationStatus.Failed ? 1 : 0;
        }

        /// <summary>
        /// Validate-only mode: re-reads both QB instances and compares data.
        /// </summary>
        private static int RunValidateOnly()
        {
            ConsoleBanner.ShowStep(1, 1, "Validate QB 2021 Data Against QB 2023");

            // Load or re-export source data
            var sourceData = DataExporter.LoadExportedData(_config.Paths.ExportDirectory);

            // Export from QB 2021
            using var qb2021Conn = new QBConnectionManager(
                _config.QuickBooks.QB2021, "QB2021-Validate");
            qb2021Conn.Connect();

            var reExporter = new DataExporter(
                qb2021Conn, _config.Export,
                Path.Combine(_config.Paths.ExportDirectory, "QB2021_Validation"),
                _config.QuickBooks.QB2021.SDKVersion);

            var targetData = reExporter.ExportAll();

            // Run validation
            using var srcConn = new QBConnectionManager(_config.QuickBooks.QB2023, "QB2023-V");
            using var tgtConn = new QBConnectionManager(_config.QuickBooks.QB2021, "QB2021-V");
            srcConn.Connect();
            tgtConn.Connect();

            var validator = new DataValidator(srcConn, tgtConn, _config.Validation,
                _config.Paths.ValidationReportDirectory);

            var report = validator.ValidateAll(sourceData, targetData);

            return report.IsValid ? 0 : 1;
        }

        /// <summary>
        /// Schema-only mode: extracts and saves QB 2021 schema.
        /// </summary>
        private static int RunSchemaOnly()
        {
            ConsoleBanner.ShowStep(1, 1, "Extract QB 2021 Schema");

            using var qb2021Conn = new QBConnectionManager(
                _config.QuickBooks.QB2021, "QB2021-Schema");
            qb2021Conn.Connect();

            var extractor = new SchemaExtractor(
                qb2021Conn,
                _config.QuickBooks.QB2021.SDKVersion,
                _config.Paths.SchemaDirectory);

            var schema = extractor.ExtractAllSchemas();
            ConsoleBanner.ShowSuccess($"Schema extracted: {schema.EntitySchemas.Count} entity types");

            return 0;
        }

        // ─── Helper Methods ──────────────────────────────────────────

        private static AppConfiguration LoadConfiguration()
        {
            var configBuilder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);

            var configuration = configBuilder.Build();
            var config = new AppConfiguration();
            configuration.Bind(config);

            // Create output directories
            Directory.CreateDirectory(config.Paths.ExportDirectory);
            Directory.CreateDirectory(config.Paths.SchemaDirectory);
            Directory.CreateDirectory(config.Paths.LogDirectory);
            Directory.CreateDirectory(config.Paths.ValidationReportDirectory);

            return config;
        }

        private static void InitializeLogging(LoggingConfig logConfig, string logDirectory)
        {
            var logLevel = logConfig.MinimumLevel switch
            {
                "Debug" => LogEventLevel.Debug,
                "Warning" => LogEventLevel.Warning,
                "Error" => LogEventLevel.Error,
                _ => LogEventLevel.Information
            };

            var loggerConfig = new LoggerConfiguration()
                .MinimumLevel.Is(logLevel);

            if (logConfig.EnableConsoleOutput)
            {
                loggerConfig.WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");
            }

            if (logConfig.EnableFileOutput)
            {
                var logFile = Path.Combine(logDirectory,
                    logConfig.LogFileName.Replace("{Date}", DateTime.Now.ToString("yyyyMMdd")));

                loggerConfig.WriteTo.File(
                    logFile,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                    fileSizeLimitBytes: logConfig.MaxFileSizeMB * 1024 * 1024,
                    retainedFileCountLimit: logConfig.RetainedFileCount,
                    rollOnFileSizeLimit: true);
            }

            Log.Logger = loggerConfig.CreateLogger();
        }

        private static RunMode ParseMode(string[] args)
        {
            if (args.Length == 0) return RunMode.Full;

            // Find the first mode argument (skip --refresh, --cleanup which are handled separately)
            var modeArg = args.FirstOrDefault(a =>
                !a.Equals("--refresh", StringComparison.OrdinalIgnoreCase) &&
                !a.Equals("--cleanup", StringComparison.OrdinalIgnoreCase));

            if (modeArg == null) return RunMode.Full;

            return modeArg.ToLowerInvariant() switch
            {
                "--export" or "-e" => RunMode.ExportOnly,
                "--import" or "-i" => RunMode.ImportOnly,
                "--validate" or "-v" => RunMode.ValidateOnly,
                "--schema" or "-s" => RunMode.SchemaOnly,
                "--help" or "-h" => ShowHelp(),
                _ => RunMode.Full
            };
        }

        private static RunMode ShowHelp()
        {
            Console.WriteLine(@"
  Usage: QB-TimeWarp [mode] [options]

  Modes:
    (no args)     Full migration (export → transform → import → validate)
    --export, -e  Export data from QB 2023 only
    --import, -i  Import previously exported data into QB 2021
    --validate, -v Validate QB 2021 data against QB 2023
    --schema, -s  Extract QB 2021 schema only
    --help, -h    Show this help message

  Working Copy Options:
    --refresh     Force re-copy originals to Working directories (deletes existing copies)
    --cleanup     Remove all Working directories and exit (originals untouched)

  Safety Features:
    - Original Desktop files are NEVER modified
    - Working copies are created automatically in C:\QB-TimeWarp\Working\
    - All operations use working copies only
    - Multiple validation layers prevent accidental original file access

  Folder Structure:
    C:\QB-TimeWarp\
    ├── Working\
    │   ├── Source\    ← QB 2023 working copy (from Joshua's Gold Coast)
    │   └── Target\    ← QB 2021 working copy (from Blank Template)
    ├── ExportedData\
    ├── Schemas\
    ├── Logs\
    └── Validation\

  Configuration:
    Edit appsettings.json for file paths and options.
    Edit Configuration/FieldMappings.json for field transformation rules.

  Prerequisites:
    - QuickBooks Desktop 2023 and 2021 installed
    - QuickBooks SDK installed and registered
    - Company files accessible on Desktop
    - Application authorized in both QB instances
");
            Environment.Exit(0);
            return RunMode.Full; // Never reached
        }

        private static void SaveTransformedData(Dictionary<string, ExportedEntitySet> data)
        {
            var transformedDir = Path.Combine(_config.Paths.ExportDirectory, "Transformed");
            Directory.CreateDirectory(transformedDir);

            foreach (var (entityType, entitySet) in data)
            {
                var filePath = Path.Combine(transformedDir, $"{entityType}.json");
                var json = JsonConvert.SerializeObject(entitySet, Formatting.Indented,
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
                File.WriteAllText(filePath, json);
            }

            Log.Information("Transformed data saved to: {Dir}", transformedDir);
        }

        private static void SaveMigrationReport(MigrationReport report)
        {
            var reportDir = _config.Paths.ValidationReportDirectory;
            Directory.CreateDirectory(reportDir);
            var filePath = Path.Combine(reportDir, $"MigrationReport_{DateTime.Now:yyyyMMdd_HHmmss}.json");

            var json = JsonConvert.SerializeObject(report, Formatting.Indented,
                new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,
                    Converters = { new Newtonsoft.Json.Converters.StringEnumConverter() }
                });

            File.WriteAllText(filePath, json);
            Log.Information("Migration report saved to: {FilePath}", filePath);
        }

        private static void ShowFinalSummary(MigrationReport report, DateTime startTime)
        {
            var duration = DateTime.UtcNow - startTime;

            Console.WriteLine();
            var summaryDict = new Dictionary<string, string>
            {
                ["Status"] = report.OverallStatus.ToString(),
                ["Total Duration"] = $"{duration.TotalMinutes:F1} minutes",
                ["Records Attempted"] = report.TotalRecordsAttempted.ToString(),
                ["Records Succeeded"] = report.TotalRecordsSucceeded.ToString(),
                ["Records Failed"] = report.TotalRecordsFailed.ToString(),
                ["Reactivated Entities"] = report.TransformationReport?.TotalReactivatedEntities.ToString() ?? "0",
                ["Accounting Model"] = report.SourcePreferences != null
                    ? $"{report.SourcePreferences.AccountingMethod} (matched: {(report.TargetPreferences?.AccountingMethod == report.SourcePreferences?.AccountingMethod ? "YES" : "VERIFY")})"
                    : "Not checked",
                ["Class Tracking"] = report.TransformationReport?.ClassTracking != null
                    ? $"{report.TransformationReport.ClassTracking.TotalClassesInSource} classes ({report.TransformationReport.ClassTracking.ClassesCreatedInTarget} created)"
                    : "Not applicable",
                ["Validation"] = report.ValidationReport?.IsValid == true ? "PASSED" : "See report",
                ["Journal Integrity"] = report.ValidationReport?.JournalIntegrity?.IsBalanced == true
                    ? "BALANCED" : (report.ValidationReport?.JournalIntegrity != null ? "ISSUES FOUND" : "Not checked"),
                ["Log File"] = Path.Combine(_config.Paths.LogDirectory, "*.log"),
                ["Report File"] = _config.Paths.ValidationReportDirectory
            };
            ConsoleBanner.ShowSummary("MIGRATION SUMMARY", summaryDict);

            // Show per-entity breakdown
            if (report.EntitySummaries.Any())
            {
                Console.WriteLine("\n  Entity Type Breakdown:");
                Console.WriteLine("  ──────────────────────────────────────────────────────");
                Console.WriteLine("  {0,-25} {1,8} {2,8} {3,8} {4,10}",
                    "Entity Type", "Total", "OK", "Failed", "Duration");
                Console.WriteLine("  ──────────────────────────────────────────────────────");

                foreach (var (entityType, summary) in report.EntitySummaries.OrderBy(e => e.Key))
                {
                    var statusIcon = summary.Failed == 0 ? "✓" : "⚠";
                    Console.WriteLine("  {0} {1,-24} {2,7} {3,7} {4,7} {5,9:F1}s",
                        statusIcon, entityType, summary.TotalAttempted,
                        summary.Succeeded, summary.Failed, summary.Duration.TotalSeconds);
                }

                Console.WriteLine("  ──────────────────────────────────────────────────────");
            }

            // Show failed records detail
            var failedRecords = report.EntitySummaries.Values
                .SelectMany(s => s.FailedRecords)
                .Take(20)
                .ToList();

            if (failedRecords.Any())
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"\n  Failed Records (showing first {Math.Min(20, failedRecords.Count)}):");
                Console.ResetColor();

                foreach (var failed in failedRecords)
                {
                    Console.WriteLine($"    - [{failed.EntityType}] {failed.SourceIdentifier}: {failed.ErrorMessage}");
                }

                if (report.EntitySummaries.Values.Sum(s => s.FailedRecords.Count) > 20)
                {
                    Console.WriteLine($"    ... and {report.TotalRecordsFailed - 20} more. See migration report for full details.");
                }
            }

            Console.WriteLine();
        }

        private enum RunMode
        {
            Full,
            ExportOnly,
            ImportOnly,
            ValidateOnly,
            SchemaOnly
        }
    }
}
