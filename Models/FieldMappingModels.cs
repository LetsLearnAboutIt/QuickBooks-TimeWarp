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
}
