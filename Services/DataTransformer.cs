using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using QB_TimeWarp.Models;
using Serilog;

namespace QB_TimeWarp.Services
{
    /// <summary>
    /// Transforms exported QB 2023 data to be compatible with QB 2021 using the FieldMappings.json config.
    /// Applies field mappings, transformations, truncations, and default values.
    /// </summary>
    public class DataTransformer
    {
        private readonly FieldMappingsConfig _mappingsConfig;
        private readonly GlobalMappingSettings _globalSettings;
        private int _unmappedFieldCount;
        private readonly HashSet<string> _loggedUnmappedFields = new();

        public DataTransformer(string mappingsFilePath)
        {
            var json = File.ReadAllText(mappingsFilePath);
            _mappingsConfig = JsonConvert.DeserializeObject<FieldMappingsConfig>(json)
                ?? throw new InvalidOperationException($"Could not load field mappings from: {mappingsFilePath}");
            _globalSettings = _mappingsConfig.GlobalSettings;

            Log.Information("Loaded field mappings with {Count} entity type configurations.",
                _mappingsConfig.EntityMappings.Count);
        }

        public DataTransformer(FieldMappingsConfig config)
        {
            _mappingsConfig = config;
            _globalSettings = config.GlobalSettings;
        }

        /// <summary>
        /// Transforms all exported data according to the field mapping configuration.
        /// </summary>
        public Dictionary<string, ExportedEntitySet> TransformAll(Dictionary<string, ExportedEntitySet> exportedData)
        {
            Log.Information("═══════════════════════════════════════════════════════");
            Log.Information("  STARTING DATA TRANSFORMATION");
            Log.Information("═══════════════════════════════════════════════════════");

            var transformed = new Dictionary<string, ExportedEntitySet>();
            _unmappedFieldCount = 0;

            foreach (var (entityType, entitySet) in exportedData)
            {
                try
                {
                    Log.Information("Transforming: {EntityType} ({Count} records)...",
                        entityType, entitySet.TotalCount);

                    var transformedSet = TransformEntitySet(entityType, entitySet);
                    transformed[entityType] = transformedSet;

                    Log.Information("  ✓ Transformed {Count} {EntityType} records",
                        transformedSet.TotalCount, entityType);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "  ✗ Error transforming {EntityType}: {Message}", entityType, ex.Message);
                    transformed[entityType] = entitySet; // Pass through unchanged on error
                }
            }

            if (_unmappedFieldCount > 0)
            {
                Log.Warning("Total unmapped fields encountered: {Count}", _unmappedFieldCount);
            }

            Log.Information("═══════════════════════════════════════════════════════");
            Log.Information("  TRANSFORMATION COMPLETE");
            Log.Information("═══════════════════════════════════════════════════════");

            return transformed;
        }

        /// <summary>
        /// Transforms a single entity set.
        /// </summary>
        public ExportedEntitySet TransformEntitySet(string entityType, ExportedEntitySet source)
        {
            // Find the mapping config for this entity type
            var mappingKey = GetMappingKey(entityType);
            EntityMappingConfig? mapping = null;

            if (mappingKey != null && _mappingsConfig.EntityMappings.ContainsKey(mappingKey))
            {
                mapping = _mappingsConfig.EntityMappings[mappingKey];
            }

            var transformedEntities = new List<QBEntity>();

            foreach (var entity in source.Entities)
            {
                try
                {
                    var transformed = TransformEntity(entity, mapping);
                    transformedEntities.Add(transformed);
                }
                catch (Exception ex)
                {
                    Log.Warning("Error transforming {EntityType} '{Name}': {Message}",
                        entityType, entity.Name, ex.Message);
                    transformedEntities.Add(entity); // Pass through on error
                }
            }

            return new ExportedEntitySet
            {
                EntityType = entityType,
                SourceVersion = "QB2023_Transformed",
                ExportTimestamp = DateTime.UtcNow,
                TotalCount = transformedEntities.Count,
                Entities = transformedEntities
            };
        }

        /// <summary>
        /// Transforms a single entity according to its field mappings.
        /// </summary>
        private QBEntity TransformEntity(QBEntity source, EntityMappingConfig? mapping)
        {
            var transformed = new QBEntity
            {
                EntityType = source.EntityType,
                ListID = source.ListID,
                TxnID = source.TxnID,
                Name = source.Name,
                FullName = source.FullName,
                ExportedAt = source.ExportedAt
            };

            if (mapping == null)
            {
                // No mapping defined - pass fields through unchanged
                transformed.Fields = source.Fields.DeepClone() as JObject ?? new JObject();
                transformed.LineItems = source.LineItems.Select(li => li.DeepClone() as JObject ?? new JObject()).ToList();
                return transformed;
            }

            // Apply field mappings
            transformed.Fields = ApplyFieldMappings(source.Fields, mapping.FieldMappings, source.Name);

            // Transform line items
            if (mapping.LineItemMappings != null && source.LineItems.Any())
            {
                transformed.LineItems = source.LineItems
                    .Select(li => ApplyFieldMappings(li, mapping.LineItemMappings, $"{source.Name}:line"))
                    .ToList();
            }
            else
            {
                transformed.LineItems = source.LineItems
                    .Select(li => li.DeepClone() as JObject ?? new JObject())
                    .ToList();
            }

            return transformed;
        }

        /// <summary>
        /// Applies field mapping rules to a JObject, producing a new JObject with mapped fields.
        /// </summary>
        private JObject ApplyFieldMappings(JObject source, List<FieldMapping> mappings, string entityName)
        {
            var result = new JObject();
            var mappedSourceFields = new HashSet<string>();

            foreach (var mapping in mappings)
            {
                mappedSourceFields.Add(mapping.SourceField);

                var sourceValue = GetNestedValue(source, mapping.SourceField);

                switch (mapping.Action.ToLowerInvariant())
                {
                    case "skip":
                        // Intentionally skip this field
                        continue;

                    case "map":
                        if (mapping.TargetField == null) continue;
                        if (sourceValue != null)
                        {
                            var processedValue = ProcessValue(sourceValue, mapping);
                            SetNestedValue(result, mapping.TargetField, processedValue);
                        }
                        break;

                    case "transform":
                        if (mapping.TargetField == null) continue;
                        if (sourceValue != null)
                        {
                            var transformedValue = ApplyTransformation(
                                sourceValue.ToString(),
                                mapping.TransformFunction ?? "",
                                mapping);
                            SetNestedValue(result, mapping.TargetField, transformedValue);
                        }
                        break;

                    case "default":
                        if (mapping.TargetField == null) continue;
                        var val = sourceValue ?? JToken.FromObject(mapping.DefaultValue ?? "");
                        SetNestedValue(result, mapping.TargetField, val);
                        break;
                }
            }

            // Handle unmapped fields
            if (_globalSettings.LogUnmappedFields)
            {
                foreach (var prop in source.Properties())
                {
                    if (!mappedSourceFields.Contains(prop.Name))
                    {
                        _unmappedFieldCount++;
                        var fieldKey = $"{entityName}.{prop.Name}";
                        if (_loggedUnmappedFields.Add(fieldKey))
                        {
                            Log.Debug("Unmapped field: {Entity} -> {Field}", entityName, prop.Name);
                        }

                        // If unmapped action is "passthrough", include the field as-is
                        if (_globalSettings.UnmappedFieldAction == "passthrough")
                        {
                            result[prop.Name] = prop.Value.DeepClone();
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Gets a value from a JObject using dot-notation path (e.g., "BillAddress.City").
        /// </summary>
        private static JToken? GetNestedValue(JObject obj, string path)
        {
            var parts = path.Split('.');
            JToken? current = obj;

            foreach (var part in parts)
            {
                if (current is JObject jObj)
                {
                    current = jObj[part];
                }
                else
                {
                    return null;
                }
            }

            return current;
        }

        /// <summary>
        /// Sets a value in a JObject using dot-notation path, creating nested objects as needed.
        /// </summary>
        private static void SetNestedValue(JObject obj, string path, JToken value)
        {
            var parts = path.Split('.');

            if (parts.Length == 1)
            {
                obj[path] = value;
                return;
            }

            JObject current = obj;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (current[parts[i]] is not JObject nested)
                {
                    nested = new JObject();
                    current[parts[i]] = nested;
                }
                current = nested;
            }

            current[parts.Last()] = value;
        }

        /// <summary>
        /// Processes a field value according to mapping rules (truncation, type conversion, etc.).
        /// </summary>
        private JToken ProcessValue(JToken value, FieldMapping mapping)
        {
            var strValue = value.ToString();

            // Truncate if needed
            if (mapping.MaxLength.HasValue && _globalSettings.TruncateLongStrings
                && strValue.Length > mapping.MaxLength.Value)
            {
                Log.Debug("Truncating field {Field} from {Original} to {Max} chars",
                    mapping.SourceField, strValue.Length, mapping.MaxLength.Value);
                strValue = strValue.Substring(0, mapping.MaxLength.Value);
                return JToken.FromObject(strValue);
            }

            return value.DeepClone();
        }

        /// <summary>
        /// Applies a named transformation function to a value.
        /// </summary>
        private JToken ApplyTransformation(string value, string functionName, FieldMapping mapping)
        {
            if (_mappingsConfig.TransformFunctions.TryGetValue(functionName, out var transformConfig))
            {
                if (transformConfig.Mappings != null)
                {
                    // Value mapping transformation
                    if (transformConfig.Mappings.TryGetValue(value, out var mappedValue))
                    {
                        return JToken.FromObject(mappedValue);
                    }

                    // Use default value if no mapping found
                    if (transformConfig.DefaultValue != null)
                    {
                        Log.Debug("No mapping for value '{Value}' in {Function}, using default: {Default}",
                            value, functionName, transformConfig.DefaultValue);
                        return JToken.FromObject(transformConfig.DefaultValue);
                    }
                }
            }

            // Apply max length truncation as fallback
            if (mapping.MaxLength.HasValue && value.Length > mapping.MaxLength.Value)
            {
                return JToken.FromObject(value.Substring(0, mapping.MaxLength.Value));
            }

            return JToken.FromObject(value);
        }

        /// <summary>
        /// Maps our entity type names (Customers, Invoices, etc.) to the mapping config keys (Customer, Invoice, etc.).
        /// </summary>
        private string? GetMappingKey(string entityType)
        {
            var keyMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Accounts"] = "Account",
                ["Customers"] = "Customer",
                ["Vendors"] = "Vendor",
                ["Employees"] = "Employee",
                ["Items"] = "Item",
                ["Invoices"] = "Invoice",
                ["Bills"] = "Bill",
                ["Payments"] = "Payment",
                ["JournalEntries"] = "JournalEntry",
                ["SalesReceipts"] = "SalesReceipt",
                ["PurchaseOrders"] = "PurchaseOrder",
                ["CreditMemos"] = "CreditMemo",
                ["Estimates"] = "Estimate",
            };

            return keyMap.TryGetValue(entityType, out var key) ? key : null;
        }
    }
}
