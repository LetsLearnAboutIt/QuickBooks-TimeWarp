using System.Diagnostics;
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
    /// Supports two modes:
    ///   --gui     : Launch WPF graphical interface (default when double-clicked)
    ///   --console : Run in console mode (legacy, default when args present)
    ///   (no args) : Launches GUI mode
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
    /// Execution flow (console):
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

        [STAThread]
        static int Main(string[] args)
        {
            // ─── GUI mode: --gui flag or no arguments (double-click launch) ───
            if (args.Length == 0 || args.Any(a => a.Equals("--gui", StringComparison.OrdinalIgnoreCase)))
            {
                var app = new QB_TimeWarp.UI.App();
                app.InitializeComponent();
                app.Run();
                return 0;
            }

            // ─── Console mode: all other flags (--export, --import, --full, etc.) ───
            try
            {
                // ─── Handle --certify BEFORE anything else (no config/working-copy needed) ───
                if (args.Any(a => a.Equals("--certify", StringComparison.OrdinalIgnoreCase)))
                {
                    return RunCertify(args);
                }

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

                // ─── DISABLED: Auto-copy from originals to Working directories ──────────
                // Working copies are already certified for QB SDK access.
                // Re-copying would overwrite certified files and require re-certification.
                // To re-enable, uncomment the block below and remove the direct-path block.
                //
                // if (_config.WorkingDirectories.AutoCreateWorkingCopies &&
                //     !string.IsNullOrEmpty(_config.SourceFiles.DesktopFolder) &&
                //     !string.IsNullOrEmpty(_config.TargetFiles.DesktopFolder))
                // {
                //     if (forceRefresh)
                //     {
                //         Log.Information("╔══════════════════════════════════════════════════════════════╗");
                //         Log.Information("║  --refresh: Killing QuickBooks processes BEFORE cleanup     ║");
                //         Log.Information("╚══════════════════════════════════════════════════════════════╝");
                //         KillQuickBooksProcesses("--refresh flag requires clean file locks before Working directory cleanup");
                //     }
                //
                //     WorkingDirectoryManager.CleanupAllWorkingArtifacts(
                //         _config.WorkingDirectories.SourcePath,
                //         _config.WorkingDirectories.TargetPath,
                //         _config.Paths.ExportDirectory);
                //     
                //     InitializeWorkingCopies(forceRefresh);
                // }
                // else
                // {
                //     Log.Information("Working copy auto-creation disabled or source/target folders not configured.");
                //     Log.Information("Using configured CompanyFilePath values directly.");
                //     ValidateCompanyFilePathsAreNotDesktopOriginals();
                // }

                // ─── ACTIVE: Use pre-certified working copies directly ─────────────────
                // Paths configured in appsettings.json → QuickBooks.QB2023/QB2021.CompanyFilePath
                // already point to C:\QB-TimeWarp\Working\Source and Working\Target
                Log.Information("╔══════════════════════════════════════════════════════════════╗");
                Log.Information("║  USING PRE-CERTIFIED WORKING COPIES (auto-copy disabled)    ║");
                Log.Information("╠══════════════════════════════════════════════════════════════╣");
                Log.Information("║  Source: {Source}", _config.QuickBooks.QB2023.CompanyFilePath);
                Log.Information("║  Target: {Target}", _config.QuickBooks.QB2021.CompanyFilePath);
                Log.Information("╚══════════════════════════════════════════════════════════════╝");

                // Validate the working copies actually exist before proceeding
                if (!File.Exists(_config.QuickBooks.QB2023.CompanyFilePath))
                    throw new FileNotFoundException(
                        $"Pre-certified source working copy not found: {_config.QuickBooks.QB2023.CompanyFilePath}");
                if (!File.Exists(_config.QuickBooks.QB2021.CompanyFilePath))
                    throw new FileNotFoundException(
                        $"Pre-certified target working copy not found: {_config.QuickBooks.QB2021.CompanyFilePath}");

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

            // Kill any lingering QB processes before starting
            KillQuickBooksProcesses("Ensuring clean state at migration start");

            // ─── Step 1: Load or Extract QB 2021 Schema ────────────────────────
            ConsoleBanner.ShowStep(1, totalSteps, "Load QB 2021 Schema");
            QBSchemaExport? schema = null;
            
            // Check if schema file already exists (schema extraction is ONE-TIME only)
            var schemaFileName = $"QB_Schema_QB_2021.json";
            var schemaFilePath = Path.Combine(_config.Paths.SchemaDirectory, schemaFileName);
            
            if (File.Exists(schemaFilePath))
            {
                Log.Information("Found existing schema file: {Path}", schemaFilePath);
                Log.Information("Loading cached schema (extraction not needed - QB 2021 schema never changes)");
                
                var schemaJson = File.ReadAllText(schemaFilePath);
                schema = JsonConvert.DeserializeObject<QBSchemaExport>(schemaJson);
                
                ConsoleBanner.ShowSuccess($"Loaded cached schema: {schema!.EntitySchemas.Count} entity types");
            }
            else
            {
                Log.Information("Schema file not found. Extracting schema from QB 2021 (ONE-TIME operation)...");
                
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
            }

            // Kill any lingering QB processes before switching versions
            KillQuickBooksProcesses("Ensuring clean state before QB 2023 export");

            // ─── Step 2: Export Data from QB 2023 ──────────────────────
            ConsoleBanner.ShowStep(2, totalSteps, "Export Data from QB 2023");
            Dictionary<string, ExportedEntitySet> exportedData;

            // Track source accounting method (extracted while QB 2023 is open)
            CompanyPreferences? sourcePreferences = null;

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

                // ── Extract source accounting method NOW while QB 2023 is open ──
                // (Previously this was attempted during import phase when QB 2021 was
                // already open, causing "Could not start QuickBooks" errors.)
                if (_config.TransformationRules.MatchAccountingModel)
                {
                    try
                    {
                        Log.Information("Extracting accounting preferences from QB 2023 source...");
                        var tempImporter = new DataImporter(
                            qb2023Conn, _config.Import,
                            _config.QuickBooks.QB2023.SDKVersion,
                            _config.TransformationRules);
                        sourcePreferences = tempImporter.QueryCompanyPreferences();
                        Log.Information("╔══════════════════════════════════════════════════════════════╗");
                        Log.Information("║  SOURCE ACCOUNTING METHOD: {Method,-35} ║",
                            sourcePreferences.AccountingMethod?.ToUpper() ?? "UNKNOWN");
                        Log.Information("╚══════════════════════════════════════════════════════════════╝");
                        ConsoleBanner.ShowSuccess($"Source accounting method: {sourcePreferences.AccountingMethod}");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Could not extract source accounting preferences: {Message}", ex.Message);
                        ConsoleBanner.ShowWarning($"Source accounting preferences extraction failed: {ex.Message}");
                    }
                }
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
            // *** COMMENTED OUT (Commit 1f18a89 + follow-up) ***
            // REASON: This step requires opening QB 2021 during the EXPORT phase (Step 2),
            // which violates our strict workflow: QB 2023 → Export → Close → Transform → QB 2021 → Import.
            // 
            // WORKFLOW REQUIREMENT: Only ONE QuickBooks instance can be open at a time for SDK operations.
            // The correct sequence is:
            //   1. Load cached schema (no QB needed - C:\QB-TimeWarp\Schemas\QB_Schema_QB_2021.json)
            //   2. Open QB 2023 → Export → CLOSE QB 2023 completely
            //   3. Transform data (SDK 16.0 → 15.0)
            //   4. Open QB 2021 → Import → CLOSE QB 2021
            // 
            // This pre-import validation tried to open BOTH QB 2023 and QB 2021 simultaneously during
            // the export phase, causing COM errors and workflow violations.
            // 
            // Post-import validation (Step 5) still runs and validates the final results.
            // Pre-import validation can be run separately if needed via --validate-only mode.
            //
            // if (_config.Validation.EnableJournalValidation)
            // {
            //     ConsoleBanner.ShowStep(3, totalSteps, "Pre-Import Journal Validation");
            //     try
            //     {
            //         using var preValidConn1 = new QBConnectionManager(_config.QuickBooks.QB2023, "QB2023-PreValid");
            //         using var preValidConn2 = new QBConnectionManager(_config.QuickBooks.QB2021, "QB2021-PreValid");
            //         preValidConn1.Connect();
            //         preValidConn2.Connect();
            //
            //         var preValidator = new DataValidator(
            //             preValidConn1, preValidConn2,
            //             _config.Validation,
            //             _config.Paths.ValidationReportDirectory);
            //
            //         var preImportJournalReport = preValidator.ValidatePreImport(transformedData);
            //
            //         if (preImportJournalReport.IsBalanced)
            //         {
            //             ConsoleBanner.ShowSuccess("Pre-import journal validation PASSED - all entries balanced");
            //         }
            //         else
            //         {
            //             ConsoleBanner.ShowWarning($"Pre-import journal validation found {preImportJournalReport.UnbalancedJournalEntries} unbalanced journal entries");
            //             ConsoleBanner.ShowWarning("Review the journal integrity report before proceeding with import.");
            //         }
            //     }
            //     catch (Exception ex)
            //     {
            //         Log.Warning(ex, "Pre-import journal validation failed (non-fatal): {Message}", ex.Message);
            //         ConsoleBanner.ShowWarning($"Pre-import journal validation skipped: {ex.Message}");
            //     }
            // }
            
            Log.Information("Pre-import validation skipped — QB 2021 not opened during export phase (strict workflow enforcement).");

            // ─── Step 3.9: Automatically Switch QuickBooks Versions ─────
            // Kill QB 2023 processes before starting QB 2021 import
            Log.Information("");
            Log.Information("╔══════════════════════════════════════════════════════════════╗");
            Log.Information("║  Switching QuickBooks versions automatically...              ║");
            Log.Information("║  Closing QB 2023 → Will open QB 2021 for import             ║");
            Log.Information("╚══════════════════════════════════════════════════════════════╝");

            KillQuickBooksProcesses("Switching from QB 2023 export to QB 2021 import");
            Log.Information("  QuickBooks processes cleared. Proceeding with import...");

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

                // Step 4a: Apply and VERIFY accounting preferences before import
                // Source preferences were already extracted during Step 2 (while QB 2023 was open)
                if (_config.TransformationRules.MatchAccountingModel)
                {
                    ConsoleBanner.ShowStep(4, totalSteps, "Applying & Verifying Accounting Preferences");
                    report = new MigrationReport { StartTime = overallStart };

                    try
                    {
                        if (sourcePreferences != null)
                        {
                            report.SourcePreferences = sourcePreferences;

                            // Apply source accounting method to target
                            var applied = importer.ApplyAccountingPreferences(sourcePreferences);

                            // Query target preferences to verify the setting took
                            var targetPrefs = importer.QueryCompanyPreferences();
                            report.TargetPreferences = targetPrefs;

                            // ── CRITICAL VERIFICATION: Compare source vs target accounting method ──
                            var sourceMethod = sourcePreferences.AccountingMethod ?? "Unknown";
                            var targetMethod = targetPrefs?.AccountingMethod ?? "Unknown";
                            bool methodsMatch = sourceMethod.Equals(targetMethod, StringComparison.OrdinalIgnoreCase);

                            Log.Information("╔══════════════════════════════════════════════════════════════╗");
                            Log.Information("║  ACCOUNTING METHOD VERIFICATION                              ║");
                            Log.Information("╠══════════════════════════════════════════════════════════════╣");
                            Log.Information("║  Source (QB 2023): {Source,-42} ║", sourceMethod.ToUpper());
                            Log.Information("║  Target (QB 2021): {Target,-42} ║", targetMethod.ToUpper());
                            Log.Information("║  Match:            {Match,-42} ║",
                                methodsMatch ? "✓ YES — Methods match" : "⚠ NO — MISMATCH DETECTED");
                            Log.Information("╚══════════════════════════════════════════════════════════════╝");

                            if (methodsMatch)
                            {
                                ConsoleBanner.ShowSuccess($"Accounting method VERIFIED: Source={sourceMethod}, Target={targetMethod} — MATCH ✓");
                            }
                            else
                            {
                                Log.Warning("╔══════════════════════════════════════════════════════════════╗");
                                Log.Warning("║  ⚠ ACCOUNTING METHOD MISMATCH WARNING ⚠                    ║");
                                Log.Warning("╠══════════════════════════════════════════════════════════════╣");
                                Log.Warning("║  Source uses {Source} but Target uses {Target}.", sourceMethod, targetMethod);
                                Log.Warning("║  This WILL affect how transactions are recorded.            ║");
                                Log.Warning("║  Financial reports will show different figures.              ║");
                                Log.Warning("║  Consult your accountant before proceeding!                 ║");
                                Log.Warning("╚══════════════════════════════════════════════════════════════╝");
                                ConsoleBanner.ShowWarning($"ACCOUNTING METHOD MISMATCH: Source={sourceMethod} vs Target={targetMethod}");
                                ConsoleBanner.ShowWarning("Transaction recording WILL differ — consult your accountant!");
                            }
                        }
                        else
                        {
                            Log.Warning("Source accounting preferences were not extracted during export phase.");
                            Log.Warning("Cannot verify accounting method match. Querying target only...");
                            var targetPrefs = importer.QueryCompanyPreferences();
                            report.TargetPreferences = targetPrefs;
                            ConsoleBanner.ShowWarning($"Target accounting method: {targetPrefs?.AccountingMethod ?? "Unknown"} (source not available for comparison)");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Could not verify accounting preferences: {Message}", ex.Message);
                        ConsoleBanner.ShowWarning($"Accounting preferences verification failed: {ex.Message}");
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
            // Kill QB processes from import before starting validation
            KillQuickBooksProcesses("Preparing for validation step");

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

                // Kill QB 2021 before connecting to QB 2023 for validation
                KillQuickBooksProcesses("Switching to QB 2023 for validation comparison");

                // Validate using the already-exported QB 2023 data (no need to reconnect)
                // Just compare exportedData (from Step 2) with reimportedData (from QB 2021)
                var validator = new DataValidator(
                    null!, null!,  // No live connections needed — using pre-exported data
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

        // ─── Certify Mode ────────────────────────────────────────────

        /// <summary>
        /// --certify mode: Opens a single connection to a QuickBooks company file
        /// to trigger the SDK certificate / application-authorization dialog.
        /// 
        /// This bypasses ALL normal safety checks (working copies, desktop-path
        /// protection) because its ONLY purpose is to establish trust between this
        /// application and the target QuickBooks installation.
        ///
        /// Usage:
        ///   QB-TimeWarp --certify "C:\Path\To\CompanyFile.qbw"
        ///   QB-TimeWarp --certify                                (uses QB2021 path from appsettings.json)
        /// </summary>
        private static int RunCertify(string[] args)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine();
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║       QB-TimeWarp — Certificate Approval Mode               ║");
            Console.WriteLine("╠══════════════════════════════════════════════════════════════╣");
            Console.WriteLine("║  This mode ONLY connects to a QuickBooks company file to    ║");
            Console.WriteLine("║  trigger the SDK certificate / app-authorization dialog.    ║");
            Console.WriteLine("║                                                              ║");
            Console.WriteLine("║  No data is read, written, or modified.                     ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine();

            // Determine the target file path
            string? targetFile = args
                .Where(a => !a.StartsWith("--", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();

            if (string.IsNullOrEmpty(targetFile))
            {
                // Fall back to QB2021 path from appsettings.json
                try
                {
                    var configBuilder = new ConfigurationBuilder()
                        .SetBasePath(Directory.GetCurrentDirectory())
                        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
                    var configuration = configBuilder.Build();
                    targetFile = configuration["QuickBooks:QB2021:CompanyFilePath"];
                }
                catch { /* ignore config errors in certify mode */ }
            }

            if (string.IsNullOrEmpty(targetFile))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("ERROR: No company file path specified.");
                Console.WriteLine();
                Console.WriteLine("Usage:");
                Console.WriteLine("  QB-TimeWarp --certify \"C:\\Path\\To\\CompanyFile.qbw\"");
                Console.ResetColor();
                return 1;
            }

            Console.WriteLine($"  Target file: {targetFile}");
            Console.WriteLine();

            // Verify file exists
            if (!File.Exists(targetFile))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"ERROR: Company file not found: {targetFile}");
                Console.ResetColor();
                return 1;
            }

            Console.WriteLine("  [1/3] Creating QBXML Request Processor...");

            try
            {
                // Create COM object directly (bypass QBConnectionManager safety checks)
                var qbType = Type.GetTypeFromProgID("QBXMLRP2.RequestProcessor");
                if (qbType == null)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("ERROR: QuickBooks SDK not found.");
                    Console.WriteLine("  Ensure QuickBooks Desktop and QBXML SDK are installed.");
                    Console.ResetColor();
                    return 1;
                }

                dynamic rp = Activator.CreateInstance(qbType)!;

                Console.WriteLine("  [2/3] Opening connection (this triggers the certificate dialog)...");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine();
                Console.WriteLine("  ┌─────────────────────────────────────────────────────────┐");
                Console.WriteLine("  │  IF A CERTIFICATE / AUTHORIZATION DIALOG APPEARS:       │");
                Console.WriteLine("  │                                                         │");
                Console.WriteLine("  │   1. Select 'Yes, always' to allow access               │");
                Console.WriteLine("  │   2. Click 'Continue' or 'OK'                           │");
                Console.WriteLine("  │                                                         │");
                Console.WriteLine("  │  The dialog may appear BEHIND other windows — check      │");
                Console.WriteLine("  │  the taskbar for a QuickBooks prompt.                    │");
                Console.WriteLine("  └─────────────────────────────────────────────────────────┘");
                Console.ResetColor();
                Console.WriteLine();

                // OpenConnection2(appID, appName, connType)
                // connType 1 = localQBD
                rp.OpenConnection2("", "QB-TimeWarp", 1);

                Console.WriteLine("  Connection opened. Beginning session...");

                // BeginSession(companyFile, fileMode)
                // fileMode 0 = doNotCare
                // IMPORTANT: Pass empty string to use the currently-open QB file.
                // Passing the file path causes "Could not start QuickBooks" errors
                // when QB is already running with the file open.
                string ticket;
                try
                {
                    // First try with the specific file path (works when QB is not running)
                    ticket = rp.BeginSession(targetFile, 0);
                }
                catch
                {
                    // Fall back to empty string (use currently open file in QB)
                    Console.WriteLine("  File-path session failed. Trying currently-open file...");
                    ticket = rp.BeginSession("", 0);
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  ✓ Session established! Ticket: {ticket}");
                Console.ResetColor();

                // Send a simple CompanyQuery to confirm connectivity
                Console.WriteLine("  [3/3] Querying company info to verify connection...");

                string companyQueryXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<?qbxml version=""15.0""?>
<QBXML>
  <QBXMLMsgsRq onError=""continueOnError"">
    <CompanyQueryRq requestID=""1"">
    </CompanyQueryRq>
  </QBXMLMsgsRq>
</QBXML>";

                string response = rp.ProcessRequest(ticket, companyQueryXml);

                // Parse company name from response
                try
                {
                    var xdoc = System.Xml.Linq.XDocument.Parse(response);
                    var companyName = xdoc.Descendants("CompanyName").FirstOrDefault()?.Value ?? "(unknown)";
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"  ✓ Company: {companyName}");
                    Console.ResetColor();
                }
                catch
                {
                    Console.WriteLine("  ✓ Response received (could not parse company name)");
                }

                // Clean up
                rp.EndSession(ticket);
                rp.CloseConnection();

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
                Console.WriteLine("║  ✓ CERTIFICATE APPROVAL COMPLETE                            ║");
                Console.WriteLine("║                                                              ║");
                Console.WriteLine("║  QB-TimeWarp is now authorized for this QuickBooks file.     ║");
                Console.WriteLine("║  You can proceed with migration using normal modes.          ║");
                Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
                Console.ResetColor();
                Console.WriteLine();

                return 0;
            }
            catch (System.Runtime.InteropServices.COMException ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  ✗ COM Error: {ex.Message}");
                Console.WriteLine();
                Console.WriteLine("  Troubleshooting:");
                Console.WriteLine("    - Is QuickBooks Desktop running with the company file open?");
                Console.WriteLine("    - Did you approve the certificate dialog?");
                Console.WriteLine("    - Check QuickBooks > Edit > Preferences > Integrated Applications");
                Console.ResetColor();
                return 1;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  ✗ Error: {ex.Message}");
                Console.ResetColor();
                return 1;
            }
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

            // Find the first mode argument (skip --refresh, --cleanup, --certify which are handled separately)
            var modeArg = args.FirstOrDefault(a =>
                !a.Equals("--refresh", StringComparison.OrdinalIgnoreCase) &&
                !a.Equals("--cleanup", StringComparison.OrdinalIgnoreCase) &&
                !a.Equals("--certify", StringComparison.OrdinalIgnoreCase));

            if (modeArg == null) return RunMode.Full;

            return modeArg.ToLowerInvariant() switch
            {
                "--full" or "--console" or "-f" => RunMode.Full,
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
    (no args)     Launch GUI (WPF graphical interface)
    --gui         Launch GUI explicitly
    --full        Full migration in console mode (export → transform → import → validate)
    --export, -e  Export data from QB 2023 only
    --import, -i  Import previously exported data into QB 2021
    --validate, -v Validate QB 2021 data against QB 2023
    --schema, -s  Extract QB 2021 schema only
    --certify     Trigger SDK certificate approval for a QB file (one-time setup)
    --help, -h    Show this help message

  Certificate Approval:
    --certify ""C:\Path\To\File.qbw""   Connect to specific file
    --certify                           Uses QB2021 path from appsettings.json

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
    │   ├── Source\    ← QB 2023 working copy (from Joshs_Gold_Coast)
    │   └── Target\    ← QB 2021 working copy (from QB21_Blank_Template)
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

        /// <summary>
        /// Kills all running QuickBooks processes to ensure clean state between steps.
        /// QB 2023 (64-bit) runs as "qbw.exe", QB 2021 (32-bit) runs as "QBW32.exe".
        /// Only one QB instance can run at a time for SDK operations.
        /// </summary>
        private static void KillQuickBooksProcesses(string reason)
        {
            var qbProcessNames = new[] { "qbw", "QBW32", "QBW32PremierAccountant", "QBWPremierAccountant" };
            var killed = new List<string>();

            foreach (var name in qbProcessNames)
            {
                try
                {
                    var processes = Process.GetProcessesByName(name);
                    foreach (var proc in processes)
                    {
                        try
                        {
                            Log.Information("Killing QuickBooks process: {Name} (PID {PID}) — Reason: {Reason}",
                                proc.ProcessName, proc.Id, reason);
                            proc.Kill();
                            proc.WaitForExit(15000); // Wait up to 15 seconds
                            killed.Add($"{proc.ProcessName} (PID {proc.Id})");
                        }
                        catch (Exception ex)
                        {
                            Log.Warning("Could not kill process {Name} (PID {PID}): {Error}",
                                proc.ProcessName, proc.Id, ex.Message);
                        }
                        finally
                        {
                            proc.Dispose();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug("Error checking for process {Name}: {Error}", name, ex.Message);
                }
            }

            if (killed.Any())
            {
                Log.Information("Killed {Count} QuickBooks process(es): {Processes}",
                    killed.Count, string.Join(", ", killed));
                // Give Windows time to fully release file locks
                Thread.Sleep(3000);
            }
            else
            {
                Log.Information("No QuickBooks processes found to kill.");
            }
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
