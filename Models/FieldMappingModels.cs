using Newtonsoft.Json;

namespace QB_TimeWarp.Models
{
    /// <summary>
    /// Root of the FieldMappings.json configuration.
    /// </summary>
    public class FieldMappingsConfig
    {
        [JsonProperty("globalSettings")]
        public GlobalMappingSettings GlobalSettings { get; set; } = new();

        [JsonProperty("entityMappings")]
        public Dictionary<string, EntityMappingConfig> EntityMappings { get; set; } = new();

        [JsonProperty("transformFunctions")]
        public Dictionary<string, TransformFunctionConfig> TransformFunctions { get; set; } = new();

        [JsonProperty("formatRules")]
        public FormatRulesConfig FormatRules { get; set; } = new();
    }

    public class GlobalMappingSettings
    {
        [JsonProperty("dateFormat")]
        public string DateFormat { get; set; } = "yyyy-MM-dd";

        [JsonProperty("truncateLongStrings")]
        public bool TruncateLongStrings { get; set; } = true;

        [JsonProperty("unmappedFieldAction")]
        public string UnmappedFieldAction { get; set; } = "skip";

        [JsonProperty("logUnmappedFields")]
        public bool LogUnmappedFields { get; set; } = true;
    }

    public class EntityMappingConfig
    {
        [JsonProperty("qb2023Entity")]
        public string QB2023Entity { get; set; } = string.Empty;

        [JsonProperty("qb2021Entity")]
        public string QB2021Entity { get; set; } = string.Empty;

        [JsonProperty("fieldMappings")]
        public List<FieldMapping> FieldMappings { get; set; } = new();

        [JsonProperty("lineItemMappings")]
        public List<FieldMapping>? LineItemMappings { get; set; }
    }

    public class FieldMapping
    {
        [JsonProperty("sourceField")]
        public string SourceField { get; set; } = string.Empty;

        [JsonProperty("targetField")]
        public string? TargetField { get; set; }

        [JsonProperty("action")]
        public string Action { get; set; } = "map";

        [JsonProperty("maxLength")]
        public int? MaxLength { get; set; }

        [JsonProperty("defaultValue")]
        public string? DefaultValue { get; set; }

        [JsonProperty("transformFunction")]
        public string? TransformFunction { get; set; }

        [JsonProperty("notes")]
        public string? Notes { get; set; }

        /// <summary>
        /// Optional: specifies the data type hint for format preservation (e.g., "date", "currency", "phone", "postalcode").
        /// When set, the transformer will apply format-aware processing.
        /// </summary>
        [JsonProperty("formatType")]
        public string? FormatType { get; set; }
    }

    public class TransformFunctionConfig
    {
        [JsonProperty("description")]
        public string Description { get; set; } = string.Empty;

        [JsonProperty("mappings")]
        public Dictionary<string, string>? Mappings { get; set; }

        [JsonProperty("defaultValue")]
        public string? DefaultValue { get; set; }

        [JsonProperty("parameters")]
        public Dictionary<string, string>? Parameters { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Format Preservation Rules
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Configuration for field format preservation during transformation.
    /// Controls how dates, currencies, phone numbers, and other formatted fields
    /// are handled when migrating from QB 2023 to QB 2021.
    /// </summary>
    public class FormatRulesConfig
    {
        /// <summary>
        /// When true, date fields maintain their original format from QB 2023.
        /// QBXML standard date format (YYYY-MM-DD) is preserved as-is.
        /// </summary>
        [JsonProperty("preserveDateFormat")]
        public bool PreserveDateFormat { get; set; } = true;

        /// <summary>
        /// The date format standard to enforce: "QBXML" (yyyy-MM-dd) or "ISO8601" (yyyy-MM-ddTHH:mm:ss).
        /// </summary>
        [JsonProperty("dateFormatStandard")]
        public string DateFormatStandard { get; set; } = "QBXML";

        /// <summary>
        /// When true, currency/amount fields preserve their decimal precision and format.
        /// </summary>
        [JsonProperty("preserveCurrencyFormat")]
        public bool PreserveCurrencyFormat { get; set; } = true;

        /// <summary>
        /// The number of decimal places for currency fields.
        /// </summary>
        [JsonProperty("currencyDecimalPlaces")]
        public int CurrencyDecimalPlaces { get; set; } = 2;

        /// <summary>
        /// When true, phone number formatting is preserved (dashes, parentheses, extensions).
        /// </summary>
        [JsonProperty("preservePhoneFormat")]
        public bool PreservePhoneFormat { get; set; } = true;

        /// <summary>
        /// When true, postal code formats are preserved (including leading zeros, hyphens).
        /// </summary>
        [JsonProperty("preservePostalCodeFormat")]
        public bool PreservePostalCodeFormat { get; set; } = true;

        /// <summary>
        /// Controls how field truncation handles formatted content.
        /// "PreserveFormat" = truncate but maintain format integrity.
        /// "Standard" = simple character truncation.
        /// </summary>
        [JsonProperty("truncationBehavior")]
        public string TruncationBehavior { get; set; } = "PreserveFormat";

        /// <summary>
        /// When true, preserves line breaks and special characters in memo/notes fields.
        /// </summary>
        [JsonProperty("preserveMemoFormatting")]
        public bool PreserveMemoFormatting { get; set; } = true;

        /// <summary>
        /// When true, ensures UTF-8 encoding is maintained for special characters.
        /// </summary>
        [JsonProperty("preserveEncoding")]
        public bool PreserveEncoding { get; set; } = true;

        /// <summary>
        /// When true, strip timezone suffixes from datetime fields for QB 2021 compatibility.
        /// </summary>
        [JsonProperty("stripTimezoneFromDates")]
        public bool StripTimezoneFromDates { get; set; } = true;

        /// <summary>
        /// List of date field name patterns used for automatic date field detection.
        /// </summary>
        [JsonProperty("dateFieldPatterns")]
        public List<string> DateFieldPatterns { get; set; } = new()
        {
            "Date", "DueDate", "TxnDate", "ShipDate", "ServiceDate",
            "HiredDate", "ReleasedDate", "BirthDate", "OpenBalanceDate",
            "ExpectedDate", "TimeCreated", "TimeModified"
        };

        /// <summary>
        /// List of currency/amount field name patterns for automatic detection.
        /// </summary>
        [JsonProperty("currencyFieldPatterns")]
        public List<string> CurrencyFieldPatterns { get; set; } = new()
        {
            "Amount", "Balance", "Price", "Rate", "Cost", "Total",
            "CreditLimit", "OpenBalance", "Subtotal"
        };
    }
}
