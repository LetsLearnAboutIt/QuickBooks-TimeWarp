using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using QB_TimeWarp.Models;
using Serilog;

namespace QB_TimeWarp.Services
{
    /// <summary>
    /// Transforms exported QB 2023 data to be compatible with QB 2021 using the FieldMappings.json config.
    /// Applies field mappings, transformations, truncations, default values, entity reactivation,
    /// class tracking preservation, and accounting model matching.
    /// </summary>
    public class DataTransformer
    {
        private readonly FieldMappingsConfig _mappingsConfig;
        private readonly GlobalMappingSettings _globalSettings;
        private readonly TransformationRulesConfig _transformationRules;
        private int _unmappedFieldCount;
        private readonly HashSet<string> _loggedUnmappedFields = new();

        // Transformation report tracking
        private readonly TransformationReport _transformationReport = new();
        private readonly Dictionary<string, int> _reactivatedCountByType = new();
        private readonly HashSet<string> _discoveredClasses = new();
        private readonly Dictionary<string, int> _classUsageByTxnType = new();
        private readonly Dictionary<string, int> _classUsageByClassName = new();

        /// <summary>
        /// Entity types that support active/inactive status.
        /// </summary>
        private static readonly HashSet<string> ActiveStatusEntityTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "Customers", "Vendors", "Items", "Accounts", "Employees"
        };

        /// <summary>
        /// Transaction entity types that support class tracking at the header level.
        /// </summary>
        private static readonly HashSet<string> ClassTrackingHeaderTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "Invoices", "Bills", "SalesReceipts", "PurchaseOrders", "JournalEntries",
            "CreditMemos", "Estimates", "Checks", "VendorCredits"
        };

        public DataTransformer(string mappingsFilePath)
            : this(mappingsFilePath, new TransformationRulesConfig())
        {
        }

        public DataTransformer(string mappingsFilePath, TransformationRulesConfig transformationRules)
        {
            var json = File.ReadAllText(mappingsFilePath);
            _mappingsConfig = JsonConvert.DeserializeObject<FieldMappingsConfig>(json)
                ?? throw new InvalidOperationException($"Could not load field mappings from: {mappingsFilePath}");
            _globalSettings = _mappingsConfig.GlobalSettings;
            _transformationRules = transformationRules;

            Log.Information("Loaded field mappings with {Count} entity type configurations.",
                _mappingsConfig.EntityMappings.Count);
            Log.Information("Transformation rules: ReactivateInactive={Reactivate}, PreserveClasses={Classes}, MatchAccounting={Accounting}",
                _transformationRules.ReactivateInactiveEntities,
                _transformationRules.PreserveClassTracking,
                _transformationRules.MatchAccountingModel);
        }

        public DataTransformer(FieldMappingsConfig config)
            : this(config, new TransformationRulesConfig())
        {
        }

        public DataTransformer(FieldMappingsConfig config, TransformationRulesConfig transformationRules)
        {
            _mappingsConfig = config;
            _globalSettings = config.GlobalSettings;
            _transformationRules = transformationRules;
        }

        /// <summary>
        /// Gets the transformation report generated during the last TransformAll call.
        /// </summary>
        public TransformationReport GetTransformationReport() => _transformationReport;

        /// <summary>
        /// Transforms all exported data according to the field mapping configuration.
        /// Applies reactivation, class tracking, and accounting model rules.
        /// </summary>
        public Dictionary<string, ExportedEntitySet> TransformAll(Dictionary<string, ExportedEntitySet> exportedData)
        {
            Log.Information("═══════════════════════════════════════════════════════");
            Log.Information("  STARTING DATA TRANSFORMATION");
            Log.Information("═══════════════════════════════════════════════════════");

            if (_transformationRules.ReactivateInactiveEntities)
                Log.Information("  ► Inactive→Active reactivation is ENABLED");
            if (_transformationRules.PreserveClassTracking)
                Log.Information("  ► Class tracking preservation is ENABLED");
            if (_transformationRules.MatchAccountingModel)
                Log.Information("  ► Accounting model matching is ENABLED");

            var transformed = new Dictionary<string, ExportedEntitySet>();
            _unmappedFieldCount = 0;
            _reactivatedCountByType.Clear();
            _discoveredClasses.Clear();
            _classUsageByTxnType.Clear();
            _classUsageByClassName.Clear();

            // Extract company preferences if available (for accounting model)
            if (_transformationRules.MatchAccountingModel)
            {
                ExtractAccountingModel(exportedData);
            }

            // Discover classes from source data
            if (_transformationRules.PreserveClassTracking)
            {
                DiscoverClassesFromData(exportedData);
            }

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

            // Build transformation report
            BuildTransformationReport(exportedData);

            // Log reactivation summary
            if (_transformationRules.ReactivateInactiveEntities && _reactivatedCountByType.Any())
            {
                LogReactivationSummary();
            }

            // Log class tracking summary
            if (_transformationRules.PreserveClassTracking && _discoveredClasses.Any())
            {
                LogClassTrackingSummary();
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
            int reactivatedCount = 0;

            foreach (var entity in source.Entities)
            {
                try
                {
                    var transformed = TransformEntity(entity, mapping, entityType);

                    // Apply inactive→active reactivation
                    if (_transformationRules.ReactivateInactiveEntities
                        && ActiveStatusEntityTypes.Contains(entityType)
                        && !entity.IsActive)
                    {
                        transformed.IsActive = true;
                        transformed.Fields["IsActive"] = JToken.FromObject("true");
                        reactivatedCount++;

                        Log.Debug("  Reactivated inactive {EntityType}: {Name}",
                            entityType, entity.Name);

                        _transformationReport.ReactivatedEntityDetails.Add(
                            $"[{entityType}] {entity.FullName ?? entity.Name}");
                    }
                    else if (ActiveStatusEntityTypes.Contains(entityType))
                    {
                        // Ensure active entities stay active
                        transformed.IsActive = entity.IsActive;
                    }

                    // Track class usage in transactions
                    if (_transformationRules.PreserveClassTracking)
                    {
                        TrackClassUsage(entityType, transformed);
                    }

                    transformedEntities.Add(transformed);
                }
                catch (Exception ex)
                {
                    Log.Warning("Error transforming {EntityType} '{Name}': {Message}",
                        entityType, entity.Name, ex.Message);
                    transformedEntities.Add(entity); // Pass through on error
                }
            }

            if (reactivatedCount > 0)
            {
                _reactivatedCountByType[entityType] = reactivatedCount;
                Log.Information("  ► Reactivated {Count} inactive {EntityType} records",
                    reactivatedCount, entityType);
            }

            return new ExportedEntitySet
            {
                EntityType = entityType,
                SourceVersion = "QB2023_Transformed",
                ExportTimestamp = DateTime.UtcNow,
                TotalCount = transformedEntities.Count,
                ActiveCount = transformedEntities.Count(e => e.IsActive),
                InactiveCount = transformedEntities.Count(e => !e.IsActive),
                Entities = transformedEntities
            };
        }

        /// <summary>
        /// Transforms a single entity according to its field mappings.
        /// </summary>
        private QBEntity TransformEntity(QBEntity source, EntityMappingConfig? mapping, string entityType)
        {
            var transformed = new QBEntity
            {
                EntityType = source.EntityType,
                ListID = source.ListID,
                TxnID = source.TxnID,
                Name = source.Name,
                FullName = source.FullName,
                IsActive = source.IsActive,
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

            // Ensure class assignments are preserved in transactions
            if (_transformationRules.PreserveClassTracking && ClassTrackingHeaderTypes.Contains(entityType))
            {
                PreserveClassAssignment(source, transformed);
            }

            _transformationReport.TotalEntitiesTransformed++;

            return transformed;
        }

        /// <summary>
        /// Ensures class assignments from source are carried into the transformed entity.
        /// </summary>
        private void PreserveClassAssignment(QBEntity source, QBEntity transformed)
        {
            // Preserve header-level class
            var sourceClass = source.Fields["ClassRef"]?["FullName"]?.ToString();
            if (!string.IsNullOrEmpty(sourceClass))
            {
                if (transformed.Fields["ClassRef"] == null)
                {
                    transformed.Fields["ClassRef"] = new JObject { ["FullName"] = sourceClass };
                }
                _discoveredClasses.Add(sourceClass);
            }

            // Preserve line-item level classes
            for (int i = 0; i < source.LineItems.Count && i < transformed.LineItems.Count; i++)
            {
                var sourceLineClass = source.LineItems[i]["ClassRef"]?["FullName"]?.ToString();
                if (!string.IsNullOrEmpty(sourceLineClass))
                {
                    if (transformed.LineItems[i]["ClassRef"] == null)
                    {
                        transformed.LineItems[i]["ClassRef"] = new JObject { ["FullName"] = sourceLineClass };
                    }
                    _discoveredClasses.Add(sourceLineClass);
                }

                // Handle JournalEntry-specific class refs
                var debitClass = source.LineItems[i]["JournalDebitLine"]?["ClassRef"]?["FullName"]?.ToString()
                    ?? source.LineItems[i]["JournalDebitLine.ClassRef.FullName"]?.ToString();
                if (!string.IsNullOrEmpty(debitClass))
                    _discoveredClasses.Add(debitClass);

                var creditClass = source.LineItems[i]["JournalCreditLine"]?["ClassRef"]?["FullName"]?.ToString()
                    ?? source.LineItems[i]["JournalCreditLine.ClassRef.FullName"]?.ToString();
                if (!string.IsNullOrEmpty(creditClass))
                    _discoveredClasses.Add(creditClass);
            }
        }

        /// <summary>
        /// Tracks class usage statistics for the transformation report.
        /// </summary>
        private void TrackClassUsage(string entityType, QBEntity entity)
        {
            void RecordClassUsage(string? className, string txnType)
            {
                if (string.IsNullOrEmpty(className)) return;

                if (!_classUsageByTxnType.ContainsKey(txnType))
                    _classUsageByTxnType[txnType] = 0;
                _classUsageByTxnType[txnType]++;

                if (!_classUsageByClassName.ContainsKey(className))
                    _classUsageByClassName[className] = 0;
                _classUsageByClassName[className]++;
            }

            // Check header-level class
            var headerClass = entity.Fields["ClassRef"]?["FullName"]?.ToString();
            RecordClassUsage(headerClass, entityType);

            // Check line-item classes
            foreach (var lineItem in entity.LineItems)
            {
                var lineClass = lineItem["ClassRef"]?["FullName"]?.ToString();
                RecordClassUsage(lineClass, $"{entityType}:LineItems");
            }
        }

        /// <summary>
        /// Discovers all classes referenced in the exported data.
        /// </summary>
        private void DiscoverClassesFromData(Dictionary<string, ExportedEntitySet> exportedData)
        {
            Log.Information("  Discovering classes from source data...");

            // Get classes from the Classes entity set if exported
            if (exportedData.ContainsKey("Classes"))
            {
                foreach (var cls in exportedData["Classes"].Entities)
                {
                    var className = cls.FullName ?? cls.Name;
                    if (!string.IsNullOrEmpty(className))
                        _discoveredClasses.Add(className);
                }
                Log.Information("    Found {Count} classes in Classes export", exportedData["Classes"].TotalCount);
            }

            // Also scan transactions for class references
            foreach (var (entityType, entitySet) in exportedData)
            {
                if (!ClassTrackingHeaderTypes.Contains(entityType)) continue;

                foreach (var entity in entitySet.Entities)
                {
                    var headerClass = entity.Fields["ClassRef"]?["FullName"]?.ToString();
                    if (!string.IsNullOrEmpty(headerClass))
                        _discoveredClasses.Add(headerClass);

                    foreach (var lineItem in entity.LineItems)
                    {
                        var lineClass = lineItem["ClassRef"]?["FullName"]?.ToString();
                        if (!string.IsNullOrEmpty(lineClass))
                            _discoveredClasses.Add(lineClass);
                    }
                }
            }

            Log.Information("    Total unique classes discovered: {Count}", _discoveredClasses.Count);
        }

        /// <summary>
        /// Extracts accounting model preferences from exported data.
        /// </summary>
        private void ExtractAccountingModel(Dictionary<string, ExportedEntitySet> exportedData)
        {
            if (!exportedData.ContainsKey("Preferences")) return;

            var prefsSet = exportedData["Preferences"];
            if (!prefsSet.Entities.Any()) return;

            var prefs = prefsSet.Entities.First();
            var accountingMethod = prefs.Fields["AccountingPrefs"]?["ReportBasis"]?.ToString()
                ?? prefs.Fields["ReportBasis"]?.ToString()
                ?? prefs.Fields["AccountingMethod"]?.ToString()
                ?? "";

            if (!string.IsNullOrEmpty(accountingMethod))
            {
                _transformationReport.SourceAccountingMethod = accountingMethod;
                Log.Information("  Source accounting method: {Method}", accountingMethod);
            }

            var classTracking = prefs.Fields["AccountingPrefs"]?["IsUsingClassTracking"]?.ToString()
                ?? prefs.Fields["IsUsingClassTracking"]?.ToString();
            if (!string.IsNullOrEmpty(classTracking))
            {
                Log.Information("  Source class tracking enabled: {Enabled}", classTracking);
            }
        }

        /// <summary>
        /// Gets all classes discovered during transformation (for import).
        /// </summary>
        public HashSet<string> GetDiscoveredClasses() => _discoveredClasses;

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
                        _transformationReport.TotalFieldsSkipped++;
                        continue;

                    case "map":
                        if (mapping.TargetField == null) continue;
                        if (sourceValue != null)
                        {
                            var processedValue = ProcessValue(sourceValue, mapping);
                            SetNestedValue(result, mapping.TargetField, processedValue);
                            _transformationReport.TotalFieldsMapped++;
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
                            _transformationReport.TotalFieldsMapped++;
                        }
                        break;

                    case "default":
                        if (mapping.TargetField == null) continue;
                        var val = sourceValue ?? JToken.FromObject(mapping.DefaultValue ?? "");
                        SetNestedValue(result, mapping.TargetField, val);
                        _transformationReport.TotalFieldsMapped++;
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
                _transformationReport.TotalFieldsTruncated++;
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
        /// Builds the detailed transformation report.
        /// </summary>
        private void BuildTransformationReport(Dictionary<string, ExportedEntitySet> exportedData)
        {
            _transformationReport.GeneratedAt = DateTime.UtcNow;
            _transformationReport.ReactivatedEntitiesByType = new Dictionary<string, int>(_reactivatedCountByType);

            // Class tracking summary
            if (_transformationRules.PreserveClassTracking)
            {
                _transformationReport.ClassTracking = new ClassTrackingSummary
                {
                    TotalClassesInSource = _discoveredClasses.Count,
                    SourceClasses = _discoveredClasses.OrderBy(c => c).ToList(),
                    ClassUsageByTransactionType = new Dictionary<string, int>(_classUsageByTxnType),
                    ClassUsageByClassName = new Dictionary<string, int>(_classUsageByClassName)
                };
            }

            // Build summary
            var parts = new List<string>();
            parts.Add($"Transformed {_transformationReport.TotalEntitiesTransformed} entities");
            parts.Add($"Mapped {_transformationReport.TotalFieldsMapped} fields");

            if (_transformationReport.TotalReactivatedEntities > 0)
                parts.Add($"Reactivated {_transformationReport.TotalReactivatedEntities} inactive entities");

            if (_discoveredClasses.Count > 0)
                parts.Add($"Preserved {_discoveredClasses.Count} classes");

            if (!string.IsNullOrEmpty(_transformationReport.SourceAccountingMethod))
                parts.Add($"Accounting method: {_transformationReport.SourceAccountingMethod}");

            _transformationReport.Summary = string.Join(" | ", parts);
        }

        /// <summary>
        /// Logs a detailed summary of reactivated entities.
        /// </summary>
        private void LogReactivationSummary()
        {
            Log.Information("────────────────────────────────────────────");
            Log.Information("  REACTIVATED ENTITIES SUMMARY");
            Log.Information("────────────────────────────────────────────");

            int total = 0;
            foreach (var (entityType, count) in _reactivatedCountByType.OrderBy(kv => kv.Key))
            {
                Log.Information("    {EntityType}: {Count} entities reactivated", entityType, count);
                total += count;
            }

            Log.Information("    ────────────────────────────────");
            Log.Information("    TOTAL: {Total} entities reactivated (inactive → active)", total);
        }

        /// <summary>
        /// Logs a summary of class tracking information.
        /// </summary>
        private void LogClassTrackingSummary()
        {
            Log.Information("────────────────────────────────────────────");
            Log.Information("  CLASS TRACKING SUMMARY");
            Log.Information("────────────────────────────────────────────");
            Log.Information("    Total unique classes: {Count}", _discoveredClasses.Count);

            foreach (var cls in _discoveredClasses.OrderBy(c => c))
            {
                var usage = _classUsageByClassName.TryGetValue(cls, out var count) ? count : 0;
                Log.Information("    Class '{Class}': used in {Count} transactions", cls, usage);
            }

            if (_classUsageByTxnType.Any())
            {
                Log.Information("    Class usage by transaction type:");
                foreach (var (txnType, count) in _classUsageByTxnType.OrderBy(kv => kv.Key))
                {
                    Log.Information("      {TxnType}: {Count} assignments", txnType, count);
                }
            }
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
