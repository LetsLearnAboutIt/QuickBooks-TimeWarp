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
}
