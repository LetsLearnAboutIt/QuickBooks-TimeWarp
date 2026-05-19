using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace QB_TimeWarp.Models
{
    /// <summary>
    /// Represents a single exported QuickBooks entity with all its fields stored as dynamic JSON.
    /// </summary>
    public class QBEntity
    {
        public string EntityType { get; set; } = string.Empty;
        public string ListID { get; set; } = string.Empty;
        public string TxnID { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public JObject Fields { get; set; } = new JObject();
        public List<JObject> LineItems { get; set; } = new List<JObject>();
        public DateTime ExportedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Container for exported data of a single entity type.
    /// </summary>
    public class ExportedEntitySet
    {
        public string EntityType { get; set; } = string.Empty;
        public string SourceVersion { get; set; } = "QB2023";
        public DateTime ExportTimestamp { get; set; } = DateTime.UtcNow;
        public int TotalCount { get; set; }
        public int ActiveCount { get; set; }
        public int InactiveCount { get; set; }
        public List<QBEntity> Entities { get; set; } = new List<QBEntity>();
    }

    /// <summary>
    /// Schema definition for a QuickBooks field.
    /// </summary>
    public class QBFieldSchema
    {
        public string FieldName { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public int? MaxLength { get; set; }
        public bool IsRequired { get; set; }
        public bool IsReadOnly { get; set; }
        public string? Description { get; set; }
        public List<string>? AllowedValues { get; set; }
        public string? ParentField { get; set; }
    }

    /// <summary>
    /// Schema definition for a QuickBooks entity type.
    /// </summary>
    public class QBEntitySchema
    {
        public string EntityType { get; set; } = string.Empty;
        public string QBXMLRequestType { get; set; } = string.Empty;
        public string QBXMLResponseType { get; set; } = string.Empty;
        public string SDKVersion { get; set; } = string.Empty;
        public List<QBFieldSchema> Fields { get; set; } = new List<QBFieldSchema>();
        public List<QBFieldSchema>? LineItemFields { get; set; }
    }

    /// <summary>
    /// Complete schema export for a QB version.
    /// </summary>
    public class QBSchemaExport
    {
        public string QuickBooksVersion { get; set; } = string.Empty;
        public string SDKVersion { get; set; } = string.Empty;
        public DateTime ExtractedAt { get; set; } = DateTime.UtcNow;
        public Dictionary<string, QBEntitySchema> EntitySchemas { get; set; } = new();
    }

    /// <summary>
    /// Result of importing a single entity.
    /// </summary>
    public class ImportResult
    {
        public string EntityType { get; set; } = string.Empty;
        public string SourceIdentifier { get; set; } = string.Empty;
        public string? NewListID { get; set; }
        public string? NewTxnID { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? ErrorCode { get; set; }
        public string? QBXMLRequest { get; set; }
        public string? QBXMLResponse { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Summary of an import batch for a single entity type.
    /// </summary>
    public class ImportBatchSummary
    {
        public string EntityType { get; set; } = string.Empty;
        public int TotalAttempted { get; set; }
        public int Succeeded { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }
        public TimeSpan Duration { get; set; }
        public List<ImportResult> FailedRecords { get; set; } = new();
    }

    /// <summary>
    /// Complete migration summary report.
    /// </summary>
    public class MigrationReport
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan TotalDuration => EndTime - StartTime;
        public string SourceCompanyFile { get; set; } = string.Empty;
        public string TargetCompanyFile { get; set; } = string.Empty;
        public MigrationStatus OverallStatus { get; set; }
        public Dictionary<string, ImportBatchSummary> EntitySummaries { get; set; } = new();
        public ValidationReport? ValidationReport { get; set; }
        public TransformationReport? TransformationReport { get; set; }
        public CompanyPreferences? SourcePreferences { get; set; }
        public CompanyPreferences? TargetPreferences { get; set; }

        public int TotalRecordsAttempted => EntitySummaries.Values.Sum(s => s.TotalAttempted);
        public int TotalRecordsSucceeded => EntitySummaries.Values.Sum(s => s.Succeeded);
        public int TotalRecordsFailed => EntitySummaries.Values.Sum(s => s.Failed);
    }

    /// <summary>
    /// Stores the ID mapping between source and target systems.
    /// </summary>
    public class IdMapping
    {
        public string EntityType { get; set; } = string.Empty;
        public string SourceListID { get; set; } = string.Empty;
        public string SourceTxnID { get; set; } = string.Empty;
        public string SourceName { get; set; } = string.Empty;
        public string TargetListID { get; set; } = string.Empty;
        public string TargetTxnID { get; set; } = string.Empty;
    }

    // ═══════════════════════════════════════════════════════════════════
    // Company Preferences & Accounting Model
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Captures company preferences from QuickBooks, including the accounting method.
    /// Used to preserve Cash vs Accrual basis during migration.
    /// </summary>
    public class CompanyPreferences
    {
        /// <summary>
        /// The accounting method: "Cash" or "Accrual".
        /// </summary>
        public string AccountingMethod { get; set; } = string.Empty;

        /// <summary>
        /// The report basis setting: "Cash" or "Accrual".
        /// </summary>
        public string ReportBasis { get; set; } = string.Empty;

        /// <summary>
        /// Whether class tracking is enabled in the company file.
        /// </summary>
        public bool IsClassTrackingEnabled { get; set; }

        /// <summary>
        /// Whether multi-currency is enabled.
        /// </summary>
        public bool IsMultiCurrencyEnabled { get; set; }

        /// <summary>
        /// The fiscal year start month (1-12).
        /// </summary>
        public int FiscalYearStartMonth { get; set; } = 1;

        /// <summary>
        /// Raw preferences data for additional inspection.
        /// </summary>
        public JObject? RawPreferences { get; set; }

        public DateTime ExtractedAt { get; set; } = DateTime.UtcNow;
    }

    // ═══════════════════════════════════════════════════════════════════
    // Class Tracking
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Represents a QuickBooks Class used for tracking transactions by department, location, etc.
    /// </summary>
    public class QBClass
    {
        public string ListID { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public string? ParentFullName { get; set; }
    }

    /// <summary>
    /// Summary of class tracking usage across the migration.
    /// </summary>
    public class ClassTrackingSummary
    {
        public int TotalClassesInSource { get; set; }
        public int TotalClassesInTarget { get; set; }
        public int ClassesCreatedInTarget { get; set; }
        public int ClassesAlreadyExisting { get; set; }
        public List<string> SourceClasses { get; set; } = new();
        public List<string> CreatedClasses { get; set; } = new();
        public List<string> MissingClasses { get; set; } = new();

        /// <summary>
        /// Class usage counts by transaction type (e.g., "Invoices" => 45).
        /// </summary>
        public Dictionary<string, int> ClassUsageByTransactionType { get; set; } = new();

        /// <summary>
        /// Class usage counts by class name (e.g., "Marketing" => 120).
        /// </summary>
        public Dictionary<string, int> ClassUsageByClassName { get; set; } = new();
    }

    // ═══════════════════════════════════════════════════════════════════
    // Transformation Report
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Detailed report of transformations applied during migration.
    /// </summary>
    public class TransformationReport
    {
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

        // Reactivated entities tracking
        public Dictionary<string, int> ReactivatedEntitiesByType { get; set; } = new();
        public int TotalReactivatedEntities => ReactivatedEntitiesByType.Values.Sum();
        public List<string> ReactivatedEntityDetails { get; set; } = new();

        // Class tracking summary
        public ClassTrackingSummary? ClassTracking { get; set; }

        // Accounting model
        public string SourceAccountingMethod { get; set; } = string.Empty;
        public string TargetAccountingMethod { get; set; } = string.Empty;
        public bool AccountingMethodMatched { get; set; }

        // General transformation stats
        public int TotalEntitiesTransformed { get; set; }
        public int TotalFieldsMapped { get; set; }
        public int TotalFieldsSkipped { get; set; }
        public int TotalFieldsTruncated { get; set; }

        public string Summary { get; set; } = string.Empty;
    }
}
