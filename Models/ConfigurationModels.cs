namespace QB_TimeWarp.Models
{
    /// <summary>
    /// Root configuration matching appsettings.json structure.
    /// </summary>
    public class AppConfiguration
    {
        public QuickBooksConfig QuickBooks { get; set; } = new();
        public PathsConfig Paths { get; set; } = new();
        public ExportConfig Export { get; set; } = new();
        public ImportConfig Import { get; set; } = new();
        public ValidationConfig Validation { get; set; } = new();
        public LoggingConfig Logging { get; set; } = new();
        public TransformationRulesConfig TransformationRules { get; set; } = new();
        public SourceFilesConfig SourceFiles { get; set; } = new();
        public TargetFilesConfig TargetFiles { get; set; } = new();
        public WorkingDirectoriesConfig WorkingDirectories { get; set; } = new();
    }

    /// <summary>
    /// Configuration for the original source QB company file location (on Desktop).
    /// These files are READ-ONLY — they are copied to working directories before use.
    /// </summary>
    public class SourceFilesConfig
    {
        /// <summary>
        /// Path to the Desktop folder containing the source QB 2023 company file.
        /// Example: "C:\Users\AIAgent\Desktop\Joshua's Gold Coast"
        /// </summary>
        public string DesktopFolder { get; set; } = string.Empty;

        /// <summary>
        /// Glob pattern to match the company file within the folder.
        /// Default: "*.qbw"
        /// </summary>
        public string CompanyFileName { get; set; } = "*.qbw";
    }

    /// <summary>
    /// Configuration for the original target QB company file location (on Desktop).
    /// These files are READ-ONLY — they are copied to working directories before use.
    /// </summary>
    public class TargetFilesConfig
    {
        /// <summary>
        /// Path to the Desktop folder containing the target QB 2021 company file.
        /// Example: "C:\Users\AIAgent\Desktop\Blank Template"
        /// </summary>
        public string DesktopFolder { get; set; } = string.Empty;

        /// <summary>
        /// Glob pattern to match the company file within the folder.
        /// Default: "*.qbw"
        /// </summary>
        public string CompanyFileName { get; set; } = "*.qbw";
    }

    /// <summary>
    /// Configuration for working directories where copies of QB files are stored.
    /// ALL migration operations use these paths — originals are NEVER touched.
    /// </summary>
    public class WorkingDirectoriesConfig
    {
        /// <summary>
        /// Path for the working source QB file copy.
        /// Default: "C:\QB-TimeWarp\Working\Source"
        /// </summary>
        public string SourcePath { get; set; } = @"C:\QB-TimeWarp\Working\Source";

        /// <summary>
        /// Path for the working target QB file copy.
        /// Default: "C:\QB-TimeWarp\Working\Target"
        /// </summary>
        public string TargetPath { get; set; } = @"C:\QB-TimeWarp\Working\Target";

        /// <summary>
        /// When true, automatically creates working copies from Desktop originals
        /// at startup before any QB operations begin.
        /// </summary>
        public bool AutoCreateWorkingCopies { get; set; } = true;

        /// <summary>
        /// Safety flag — when true, ensures originals are never modified.
        /// Should ALWAYS be true in production use.
        /// </summary>
        public bool PreserveOriginals { get; set; } = true;
    }

    /// <summary>
    /// Configuration for transformation rules applied during QB 2023 → QB 2021 migration.
    /// </summary>
    public class TransformationRulesConfig
    {
        /// <summary>
        /// When true, all inactive entities (Customers, Vendors, Items, Accounts, Employees)
        /// from QB 2023 will be set to Active in QB 2021.
        /// </summary>
        public bool ReactivateInactiveEntities { get; set; } = true;

        /// <summary>
        /// When true, class tracking assignments are preserved from QB 2023 to QB 2021.
        /// Missing classes will be created in QB 2021 automatically.
        /// </summary>
        public bool PreserveClassTracking { get; set; } = true;

        /// <summary>
        /// When true, the accounting method (Cash vs Accrual) from QB 2023 is applied to QB 2021.
        /// </summary>
        public bool MatchAccountingModel { get; set; } = true;
    }

    public class QuickBooksConfig
    {
        public QBInstanceConfig QB2023 { get; set; } = new();
        public QBInstanceConfig QB2021 { get; set; } = new();
    }

    public class QBInstanceConfig
    {
        public string CompanyFilePath { get; set; } = string.Empty;
        public string ApplicationName { get; set; } = "QB-TimeWarp";
        public string SDKVersion { get; set; } = string.Empty;
        public int MaxRetries { get; set; } = 3;
        public int TimeoutSeconds { get; set; } = 300;
    }

    public class PathsConfig
    {
        public string ExportDirectory { get; set; } = ".\\ExportedData";
        public string SchemaDirectory { get; set; } = ".\\Schemas";
        public string LogDirectory { get; set; } = ".\\Logs";
        public string ValidationReportDirectory { get; set; } = ".\\Validation";
        public string FieldMappingsFile { get; set; } = ".\\Configuration\\FieldMappings.json";
    }

    public class ExportConfig
    {
        public int BatchSize { get; set; } = 500;
        public bool IncludeInactiveRecords { get; set; } = true;
        public string ExportFormat { get; set; } = "JSON";
        public string? DateRangeStart { get; set; }
        public string? DateRangeEnd { get; set; }
        public List<string> EntityTypes { get; set; } = new();
    }

    public class ImportConfig
    {
        public int BatchSize { get; set; } = 100;
        public bool SkipOnError { get; set; } = true;
        public bool DryRun { get; set; } = false;
        public List<string> ImportOrder { get; set; } = new();
    }

    public class ValidationConfig
    {
        public bool EnableFieldByFieldComparison { get; set; } = true;
        public bool EnableFinancialReconciliation { get; set; } = true;
        public bool EnableEntityCountVerification { get; set; } = true;
        public bool EnableJournalValidation { get; set; } = true;
        public decimal ToleranceAmount { get; set; } = 0.01M;
        public string ReportFormat { get; set; } = "JSON";
    }

    public class LoggingConfig
    {
        public string MinimumLevel { get; set; } = "Information";
        public bool EnableConsoleOutput { get; set; } = true;
        public bool EnableFileOutput { get; set; } = true;
        public string LogFileName { get; set; } = "QB-TimeWarp-{Date}.log";
        public int MaxFileSizeMB { get; set; } = 100;
        public int RetainedFileCount { get; set; } = 10;
    }
}
