using QB_TimeWarp.Models;
using Serilog;

namespace QB_TimeWarp.Services
{
    /// <summary>
    /// Manages working directory creation and file copying to ensure original QuickBooks
    /// company files are NEVER directly modified.
    /// 
    /// SAFETY ARCHITECTURE:
    ///   1. Original files live on the Desktop (read-only source of truth)
    ///   2. Working copies are created in C:\QB-TimeWarp\Working\Source\ and Working\Target\
    ///   3. ALL operations (export, import, validate) use ONLY working copies
    ///   4. Multiple validation layers prevent accidental access to originals
    /// </summary>
    public class WorkingDirectoryManager
    {
        private readonly SourceFilesConfig _sourceConfig;
        private readonly TargetFilesConfig _targetConfig;
        private readonly WorkingDirectoriesConfig _workingConfig;

        /// <summary>
        /// Paths to the working copies after initialization.
        /// </summary>
        public string? WorkingSourceFilePath { get; private set; }
        public string? WorkingTargetFilePath { get; private set; }

        /// <summary>
        /// Original file paths (for reference/logging only — NEVER write to these).
        /// </summary>
        public string? OriginalSourceFilePath { get; private set; }
        public string? OriginalTargetFilePath { get; private set; }

        public WorkingDirectoryManager(
            SourceFilesConfig sourceConfig,
            TargetFilesConfig targetConfig,
            WorkingDirectoriesConfig workingConfig)
        {
            _sourceConfig = sourceConfig ?? throw new ArgumentNullException(nameof(sourceConfig));
            _targetConfig = targetConfig ?? throw new ArgumentNullException(nameof(targetConfig));
            _workingConfig = workingConfig ?? throw new ArgumentNullException(nameof(workingConfig));
        }

        /// <summary>
        /// Initializes working directories and copies original files to working locations.
        /// Returns true if working copies are ready for use.
        /// </summary>
        /// <param name="forceRefresh">If true, deletes existing working copies and re-copies from originals.</param>
        public bool InitializeWorkingCopies(bool forceRefresh = false)
        {
            Log.Information("╔══════════════════════════════════════════════════════════════╗");
            Log.Information("║  WORKING COPY INITIALIZATION — ORIGINALS WILL BE PRESERVED  ║");
            Log.Information("╚══════════════════════════════════════════════════════════════╝");

            try
            {
                // Step 1: Resolve original file paths
                ResolveOriginalFilePaths();

                // Step 2: Create working directories
                CreateWorkingDirectories();

                // Step 3: Copy files to working directories
                CopySourceFile(forceRefresh);
                CopyTargetFile(forceRefresh);

                // Step 4: Verify working copies
                VerifyWorkingCopies();

                Log.Information("╔══════════════════════════════════════════════════════════════╗");
                Log.Information("║  ✓ WORKING COPIES READY — ORIGINALS ARE SAFE               ║");
                Log.Information("╠══════════════════════════════════════════════════════════════╣");
                Log.Information("║  Source: {SourcePath}", WorkingSourceFilePath);
                Log.Information("║  Target: {TargetPath}", WorkingTargetFilePath);
                Log.Information("╚══════════════════════════════════════════════════════════════╝");

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to initialize working copies: {Message}", ex.Message);
                throw new WorkingCopyException(
                    $"Cannot proceed — working copy initialization failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Resolves the actual .qbw file paths from the configured desktop folders using glob patterns.
        /// </summary>
        private void ResolveOriginalFilePaths()
        {
            Log.Information("Resolving original file paths...");

            // Resolve source file (Joshs_Gold_Coast — testing)
            OriginalSourceFilePath = ResolveCompanyFile(
                _sourceConfig.DesktopFolder,
                _sourceConfig.CompanyFileName,
                "Source (Joshs_Gold_Coast)");

            // Resolve target file (QB21_Blank_Template)
            OriginalTargetFilePath = ResolveCompanyFile(
                _targetConfig.DesktopFolder,
                _targetConfig.CompanyFileName,
                "Target (QB21_Blank_Template)");

            Log.Information("Original Source: {Path} ({Size})",
                OriginalSourceFilePath, FormatFileSize(new FileInfo(OriginalSourceFilePath).Length));
            Log.Information("Original Target: {Path} ({Size})",
                OriginalTargetFilePath, FormatFileSize(new FileInfo(OriginalTargetFilePath).Length));
        }

        /// <summary>
        /// Finds a company file using a glob pattern in the specified folder.
        /// </summary>
        private static string ResolveCompanyFile(string folder, string pattern, string description)
        {
            if (!Directory.Exists(folder))
            {
                throw new WorkingCopyException(
                    $"{description} folder not found: {folder}. " +
                    "Ensure the folder exists on the Desktop.");
            }

            var files = Directory.GetFiles(folder, pattern);

            if (files.Length == 0)
            {
                throw new WorkingCopyException(
                    $"No company file matching '{pattern}' found in {folder}. " +
                    $"Looking for {description}.");
            }

            if (files.Length > 1)
            {
                Log.Warning("Multiple files matching '{Pattern}' found in {Folder}. Using first: {File}",
                    pattern, folder, files[0]);
            }

            return files[0];
        }

        /// <summary>
        /// Creates the Working\Source\ and Working\Target\ directories.
        /// </summary>
        private void CreateWorkingDirectories()
        {
            Log.Information("Creating working directories...");

            Directory.CreateDirectory(_workingConfig.SourcePath);
            Log.Information("  Working Source directory: {Path}", _workingConfig.SourcePath);

            Directory.CreateDirectory(_workingConfig.TargetPath);
            Log.Information("  Working Target directory: {Path}", _workingConfig.TargetPath);
        }

        /// <summary>
        /// Copies the source QB file to the working source directory.
        /// </summary>
        private void CopySourceFile(bool forceRefresh)
        {
            if (OriginalSourceFilePath == null)
                throw new WorkingCopyException("Original source file path not resolved.");

            // Explicitly filter for .QBW files only — reject any other extension
            if (!OriginalSourceFilePath.EndsWith(".qbw", StringComparison.OrdinalIgnoreCase))
                throw new WorkingCopyException(
                    $"Source file must be a .QBW file, got: {OriginalSourceFilePath}");

            var fileName = Path.GetFileName(OriginalSourceFilePath);
            WorkingSourceFilePath = Path.Combine(_workingConfig.SourcePath, fileName);

            CopyFileWithSafetyChecks(
                OriginalSourceFilePath,
                WorkingSourceFilePath,
                "Source",
                forceRefresh);

            // NOTE: We intentionally copy ONLY the .QBW file. Associated files (.ND, .TLG, .DSN)
            // are NOT copied because they are auto-generated by QuickBooks when it opens the .QBW
            // file. Copying them can cause stale lock/connection issues in the working directory.
            Log.Information("  Copying .QBW file only (associated files not needed)");
        }

        /// <summary>
        /// Copies the target QB file to the working target directory.
        /// </summary>
        private void CopyTargetFile(bool forceRefresh)
        {
            if (OriginalTargetFilePath == null)
                throw new WorkingCopyException("Original target file path not resolved.");

            // Explicitly filter for .QBW files only — reject any other extension
            if (!OriginalTargetFilePath.EndsWith(".qbw", StringComparison.OrdinalIgnoreCase))
                throw new WorkingCopyException(
                    $"Target file must be a .QBW file, got: {OriginalTargetFilePath}");

            var fileName = Path.GetFileName(OriginalTargetFilePath);
            WorkingTargetFilePath = Path.Combine(_workingConfig.TargetPath, fileName);

            CopyFileWithSafetyChecks(
                OriginalTargetFilePath,
                WorkingTargetFilePath,
                "Target",
                forceRefresh);

            // NOTE: We intentionally copy ONLY the .QBW file. Associated files (.ND, .TLG, .DSN)
            // are NOT copied because they are auto-generated by QuickBooks when it opens the .QBW
            // file. Copying them can cause stale lock/connection issues in the working directory.
            Log.Information("  Copying .QBW file only (associated files not needed)");
        }

        /// <summary>
        /// Copies a file with safety checks, progress reporting, and size verification.
        /// </summary>
        private static void CopyFileWithSafetyChecks(
            string sourcePath, string destPath, string label, bool forceRefresh)
        {
            var sourceInfo = new FileInfo(sourcePath);
            var sourceSize = sourceInfo.Length;

            if (File.Exists(destPath) && !forceRefresh)
            {
                var existingInfo = new FileInfo(destPath);
                Log.Information("  {Label} working copy already exists: {Path} ({Size})",
                    label, destPath, FormatFileSize(existingInfo.Length));

                // Verify existing copy is valid (size should be reasonable)
                if (existingInfo.Length > 0)
                {
                    Log.Information("  {Label} working copy appears valid — skipping copy. Use --refresh to force re-copy.",
                        label);
                    return;
                }

                Log.Warning("  {Label} working copy appears corrupt (size: {Size}). Re-copying...",
                    label, existingInfo.Length);
            }

            if (File.Exists(destPath) && forceRefresh)
            {
                Log.Information("  Force refresh: removing existing {Label} working copy...", label);
                File.Delete(destPath);
            }

            // Copy with progress reporting
            Log.Information("  Creating working copy of {Label} ({Size})...",
                label, FormatFileSize(sourceSize));
            Log.Information("    From: {Source}", sourcePath);
            Log.Information("    To:   {Dest}", destPath);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            File.Copy(sourcePath, destPath, overwrite: true);
            sw.Stop();

            // Verify copy
            var destInfo = new FileInfo(destPath);
            if (destInfo.Length != sourceSize)
            {
                throw new WorkingCopyException(
                    $"{label} file copy verification FAILED! " +
                    $"Source: {sourceSize} bytes, Dest: {destInfo.Length} bytes. " +
                    "File may be corrupt.");
            }

            Log.Information("  ✓ {Label} copy complete: {Size} in {Elapsed:F1}s (verified: sizes match)",
                label, FormatFileSize(destInfo.Length), sw.Elapsed.TotalSeconds);
        }

        /// <summary>
        /// Verifies that working copies exist and are valid.
        /// </summary>
        private void VerifyWorkingCopies()
        {
            Log.Information("Verifying working copies...");

            if (string.IsNullOrEmpty(WorkingSourceFilePath) || !File.Exists(WorkingSourceFilePath))
                throw new WorkingCopyException("Working source file does not exist after copy!");

            if (string.IsNullOrEmpty(WorkingTargetFilePath) || !File.Exists(WorkingTargetFilePath))
                throw new WorkingCopyException("Working target file does not exist after copy!");

            var sourceInfo = new FileInfo(WorkingSourceFilePath);
            var targetInfo = new FileInfo(WorkingTargetFilePath);

            if (sourceInfo.Length == 0)
                throw new WorkingCopyException("Working source file is empty (0 bytes)!");

            if (targetInfo.Length == 0)
                throw new WorkingCopyException("Working target file is empty (0 bytes)!");

            Log.Information("  ✓ Source working copy: {Path} ({Size})",
                WorkingSourceFilePath, FormatFileSize(sourceInfo.Length));
            Log.Information("  ✓ Target working copy: {Path} ({Size})",
                WorkingTargetFilePath, FormatFileSize(targetInfo.Length));
        }

        /// <summary>
        /// Validates that a given file path is within the working directories (NOT an original).
        /// Call this before any write/modify operation as a safety check.
        /// </summary>
        public bool IsWorkingCopyPath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return false;

            var normalizedPath = Path.GetFullPath(filePath).ToUpperInvariant();
            var normalizedSourceDir = Path.GetFullPath(_workingConfig.SourcePath).ToUpperInvariant();
            var normalizedTargetDir = Path.GetFullPath(_workingConfig.TargetPath).ToUpperInvariant();

            return normalizedPath.StartsWith(normalizedSourceDir) ||
                   normalizedPath.StartsWith(normalizedTargetDir);
        }

        /// <summary>
        /// Validates that a file path is NOT in a protected desktop folder.
        /// Throws if the path points to an original file.
        /// </summary>
        public void ValidateNotOriginalPath(string filePath, string operationDescription)
        {
            if (string.IsNullOrEmpty(filePath))
                return;

            var normalizedPath = Path.GetFullPath(filePath).ToUpperInvariant();

            // Check against source desktop folder
            if (!string.IsNullOrEmpty(_sourceConfig.DesktopFolder))
            {
                var normalizedDesktopSource = Path.GetFullPath(_sourceConfig.DesktopFolder).ToUpperInvariant();
                if (normalizedPath.StartsWith(normalizedDesktopSource))
                {
                    throw new OriginalFileProtectionException(
                        $"SAFETY VIOLATION: Operation '{operationDescription}' attempted on ORIGINAL file! " +
                        $"Path: {filePath}. This is in the protected Desktop source folder. " +
                        "Only working copies in the Working directories may be modified.");
                }
            }

            // Check against target desktop folder
            if (!string.IsNullOrEmpty(_targetConfig.DesktopFolder))
            {
                var normalizedDesktopTarget = Path.GetFullPath(_targetConfig.DesktopFolder).ToUpperInvariant();
                if (normalizedPath.StartsWith(normalizedDesktopTarget))
                {
                    throw new OriginalFileProtectionException(
                        $"SAFETY VIOLATION: Operation '{operationDescription}' attempted on ORIGINAL file! " +
                        $"Path: {filePath}. This is in the protected Desktop target folder. " +
                        "Only working copies in the Working directories may be modified.");
                }
            }

            // General Desktop folder protection
            if (normalizedPath.Contains("\\DESKTOP\\") && !normalizedPath.Contains("\\WORKING\\"))
            {
                Log.Warning("SAFETY WARNING: Path '{Path}' appears to be on the Desktop " +
                    "but not in a Working directory. Operation: {Op}", filePath, operationDescription);
            }
        }

        /// <summary>
        /// Cleans up working directories by deleting all working copies.
        /// </summary>
        public void CleanupWorkingDirectories()
        {
            Log.Information("Cleaning up working directories...");

            if (Directory.Exists(_workingConfig.SourcePath))
            {
                Directory.Delete(_workingConfig.SourcePath, recursive: true);
                Log.Information("  Deleted: {Path}", _workingConfig.SourcePath);
            }

            if (Directory.Exists(_workingConfig.TargetPath))
            {
                Directory.Delete(_workingConfig.TargetPath, recursive: true);
                Log.Information("  Deleted: {Path}", _workingConfig.TargetPath);
            }

            WorkingSourceFilePath = null;
            WorkingTargetFilePath = null;

            Log.Information("Working directories cleaned up. Originals remain untouched.");
        }

        /// <summary>
        /// Returns a summary of the current working copy state.
        /// </summary>
        public Dictionary<string, string> GetWorkingSummary()
        {
            var summary = new Dictionary<string, string>
            {
                ["Original Source"] = OriginalSourceFilePath ?? "Not resolved",
                ["Original Target"] = OriginalTargetFilePath ?? "Not resolved",
                ["Working Source"] = WorkingSourceFilePath ?? "Not created",
                ["Working Target"] = WorkingTargetFilePath ?? "Not created",
                ["Auto-Create Enabled"] = _workingConfig.AutoCreateWorkingCopies.ToString(),
                ["Preserve Originals"] = _workingConfig.PreserveOriginals.ToString()
            };

            if (WorkingSourceFilePath != null && File.Exists(WorkingSourceFilePath))
                summary["Working Source Size"] = FormatFileSize(new FileInfo(WorkingSourceFilePath).Length);

            if (WorkingTargetFilePath != null && File.Exists(WorkingTargetFilePath))
                summary["Working Target Size"] = FormatFileSize(new FileInfo(WorkingTargetFilePath).Length);

            return summary;
        }

        /// <summary>
        /// Formats a file size in human-readable form.
        /// </summary>
        private static string FormatFileSize(long bytes)
        {
            if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
            if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:F1} MB";
            if (bytes >= 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes} bytes";
        }
    }

    /// <summary>
    /// Exception thrown when working copy creation or validation fails.
    /// </summary>
    public class WorkingCopyException : Exception
    {
        public WorkingCopyException(string message) : base(message) { }
        public WorkingCopyException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Exception thrown when an operation attempts to modify an original (protected) file.
    /// This is a CRITICAL safety exception that should halt all operations.
    /// </summary>
    public class OriginalFileProtectionException : Exception
    {
        public OriginalFileProtectionException(string message) : base(message) { }
    }
}
