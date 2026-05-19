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
