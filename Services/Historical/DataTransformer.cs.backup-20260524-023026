using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using QB_TimeWarp.Helpers;
using QB_TimeWarp.Models;
using Serilog;

namespace QB_TimeWarp.Services
{
    /// <summary>
    /// Transforms exported QB 2023 data to be compatible with QB 2021 using the FieldMappings.json config.
    /// Applies field mappings, transformations, truncations, default values, entity reactivation,
    /// class tracking preservation, accounting model matching, field format preservation,
    /// and QB SDK 15.0 compatibility filtering (removes fields not supported in QB 2021).
    /// 
    /// CHANGELOG:
    /// - Fix #38 (commit 3364594): Convert CreditCardCharge/CreditCardCredit to JournalEntry format
    ///   QB 2021 doesn't support CC transaction types. Convert during transformation to preserve
    ///   accounting integrity. "The journal is the truth" - Joseph. See ConvertCreditCardToJournalEntry()
    /// - Fix #37 (commit cb9acaf): Remove unsupported entity types (CreditCardCharges, ItemReceipts, etc.)
    ///   Added schema validation to ensure only QB 2021-supported types are processed
    /// - Fix #14 (Revised): Zero out Employee Notes field - historical data not needed
    /// - Fix #1-#13: Various field mappings, format preservation, entity reactivation
    /// </summary>
    public class DataTransformer
    {
        private readonly FieldMappingsConfig _mappingsConfig;
        private readonly GlobalMappingSettings _globalSettings;
        private readonly FormatRulesConfig _formatRules;
        private readonly TransformationRulesConfig _transformationRules;
        private int _unmappedFieldCount;
        private readonly HashSet<string> _loggedUnmappedFields = new();
        private int _sdk16FieldsRemoved;
        private int _fieldsLengthAdjusted;
        private int _emptyNameFieldsFixed;
        private int _payrollFieldsSimplified;
        private int _ccBalanceSignsFixed;
        private int _hierarchicalNamesParsed;
        private int _isActiveOverrides;
        private int _nameToMemoPreserved;
        private int _employeeNotesTruncated;  // FIX #14: Tracks Notes field truncations

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
            "CreditMemos", "Estimates", "Checks", "CreditCardCharges", "CreditCardCredits",
            "BillPaymentChecks", "BillPaymentCreditCards", "VendorCredits"
        };

        /// <summary>
        /// Transaction types that have a read-only "Name" field AND a writable "Memo" field.
        /// Name data from these transactions is preserved by prepending it to the Memo field
        /// during transformation, preventing data loss from Fix #8's read-only field exclusion.
        /// Note: Transfers do NOT have a Memo field and are excluded (already at 100% success).
        /// </summary>
        private static readonly HashSet<string> NameToMemoTransactionTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "Checks", "CreditCardCharges", "CreditCardCredits",
            "BillPaymentChecks", "BillPaymentCreditCards",
            "Deposits", "SalesReceipts", "JournalEntries"
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
            _formatRules = _mappingsConfig.FormatRules;
            _transformationRules = transformationRules;

            Log.Information("Loaded field mappings with {Count} entity type configurations.",
                _mappingsConfig.EntityMappings.Count);
            Log.Information("Transformation rules: ReactivateInactive={Reactivate}, PreserveClasses={Classes}, MatchAccounting={Accounting}",
                _transformationRules.ReactivateInactiveEntities,
                _transformationRules.PreserveClassTracking,
                _transformationRules.MatchAccountingModel);
            Log.Information("Format preservation: Dates={Dates}, Currency={Currency}, Phone={Phone}, PostalCode={Postal}, Encoding={Encoding}",
                _formatRules.PreserveDateFormat,
                _formatRules.PreserveCurrencyFormat,
                _formatRules.PreservePhoneFormat,
                _formatRules.PreservePostalCodeFormat,
                _formatRules.PreserveEncoding);
        }

        public DataTransformer(FieldMappingsConfig config)
            : this(config, new TransformationRulesConfig())
        {
        }

        public DataTransformer(FieldMappingsConfig config, TransformationRulesConfig transformationRules)
        {
            _mappingsConfig = config;
            _globalSettings = config.GlobalSettings;
            _formatRules = config.FormatRules;
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

            // ═══════════════════════════════════════════════════════════════════
            // FIX #40: Merge converted entities into their NEW EntityType key
            // ═══════════════════════════════════════════════════════════════════
            // FIX #38 converts CreditCardCharges/Credits → JournalEntries by setting
            // ExportedEntitySet.EntityType = "JournalEntries", but the dictionary key
            // remains the ORIGINAL type (e.g., "CreditCardCharges"). The DataImporter
            // uses the dictionary KEY — not the EntityType property — to determine
            // QBXML request type, causing "No Add request mapping" errors for 378+
            // CreditCard transactions.
            //
            // SOLUTION: After all transformations, detect type mismatches (key ≠
            // EntityType), merge those entities into the correct target key, and
            // remove the now-empty original keys from the dictionary.
            // ═══════════════════════════════════════════════════════════════════
            var keysToRemove = new List<string>();
            foreach (var (key, entitySet) in transformed)
            {
                // Check if the EntityType changed during transformation (e.g., CreditCardCharges → JournalEntries)
                if (!string.IsNullOrEmpty(entitySet.EntityType) &&
                    !key.Equals(entitySet.EntityType, StringComparison.OrdinalIgnoreCase))
                {
                    var targetKey = entitySet.EntityType;
                    Log.Information("  ► FIX #40: Merging {Count} converted '{OriginalKey}' entities into '{TargetKey}' dictionary key",
                        entitySet.Entities.Count, key, targetKey);

                    if (transformed.ContainsKey(targetKey))
                    {
                        // Target key already exists — merge entities into it
                        var existingSet = transformed[targetKey];
                        existingSet.Entities.AddRange(entitySet.Entities);
                        existingSet.TotalCount = existingSet.Entities.Count;
                        existingSet.ActiveCount = existingSet.Entities.Count(e => e.IsActive);
                        existingSet.InactiveCount = existingSet.Entities.Count(e => !e.IsActive);

                        Log.Information("  ✓ FIX #40: Merged into existing '{TargetKey}' (now {Total} total records)",
                            targetKey, existingSet.TotalCount);
                    }
                    else
                    {
                        // Target key doesn't exist yet — create it with the converted entities
                        transformed[targetKey] = entitySet;

                        Log.Information("  ✓ FIX #40: Created new '{TargetKey}' key with {Count} converted records",
                            targetKey, entitySet.Entities.Count);
                    }

                    // Mark original key for removal (can't modify dictionary during iteration)
                    keysToRemove.Add(key);
                }
            }

            // Remove the original keys that have been merged into their target types
            foreach (var key in keysToRemove)
            {
                transformed.Remove(key);
                Log.Information("  ✓ FIX #40: Removed empty original key '{Key}' from dictionary", key);
            }

            if (_unmappedFieldCount > 0)
            {
                Log.Warning("Total unmapped fields encountered: {Count}", _unmappedFieldCount);
            }

            // Build transformation report
            BuildTransformationReport(exportedData);

            // Log QB 2021 compatibility summary
            LogQB2021CompatibilitySummary();

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

            // Log format preservation summary
            LogFormatPreservationSummary();

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

            // FIX #38: Convert CreditCardCharge/CreditCardCredit to JournalEntry
            // QB 2021 doesn't support these transaction types, but "the journal is the truth"
            // Convert them to journal entries to preserve accounting integrity
            string outputEntityType = entityType;
            if (entityType.Equals("CreditCardCharges", StringComparison.OrdinalIgnoreCase) ||
                entityType.Equals("CreditCardCredits", StringComparison.OrdinalIgnoreCase))
            {
                Log.Information("  ► Converting {Count} {EntityType} to JournalEntry format (Fix #38: QB 2021 compatibility)",
                    transformedEntities.Count, entityType);

                var convertedEntities = new List<QBEntity>();
                foreach (var entity in transformedEntities)
                {
                    try
                    {
                        var journalEntry = ConvertCreditCardToJournalEntry(entity, entityType);
                        convertedEntities.Add(journalEntry);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "  ✗ Error converting {EntityType} TxnID {TxnID} to JournalEntry: {Message}",
                            entityType, entity.TxnID, ex.Message);
                        // Skip this entity rather than failing the entire migration
                    }
                }

                transformedEntities = convertedEntities;
                outputEntityType = "JournalEntries";  // These will be imported as journal entries
                Log.Information("  ✓ Converted {Count} {Source} records to JournalEntry format",
                    convertedEntities.Count, entityType);
            }

            return new ExportedEntitySet
            {
                EntityType = outputEntityType,  // Changed to JournalEntries if converted
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
        /// Includes QB 2021 SDK 15.0 compatibility filtering.
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
            }
            else
            {
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
            }

            // ── FIX #1: Parse hierarchical names for easier import ──────────
            // Split "Parent:Child:Leaf" into separate fields so the importer
            // can construct proper <Name> + <ParentRef> QBXML
            ParseHierarchicalName(transformed, entityType);

            // ── FIX #2: Force IsActive = true during transformation ───────
            // Accountant requirement: all entities must be active for import.
            // This ensures even originally inactive entities get imported.
            ForceIsActive(transformed, entityType);

            // ── QB 2021 SDK 15.0 Compatibility Fixes ────────────────────
            // Remove SDK 16.0-only fields that would cause 0x80040400 errors
            RemoveSDK16OnlyFields(transformed.Fields, entityType);
            foreach (var lineItem in transformed.LineItems)
            {
                RemoveSDK16OnlyFields(lineItem, $"{entityType}:line");
            }

            // Enforce QB 2021 field length limits
            EnforceQB2021FieldLengths(transformed.Fields, entityType);
            foreach (var lineItem in transformed.LineItems)
            {
                EnforceQB2021FieldLengths(lineItem, $"{entityType}:line");
            }

            // Fix empty Name fields (causes QB import to fail)
            FixEmptyNameField(transformed, entityType);

            // Simplify payroll fields for QB 2021 compatibility
            SimplifyPayrollFields(transformed.Fields, entityType);

            // Fix CC balance sign inversion (accountant flagged this)
            FixCCBalanceSigns(transformed, entityType);

            // Ensure account numbers are preserved (accountant flagged missing)
            PreserveAccountNumbers(source, transformed, entityType);

            // ── Name → Memo Preservation ──────────────────────────────────
            // Transactions like Check, Deposit, SalesReceipt, JournalEntry have a
            // read-only "Name" field that Fix #8 excludes to avoid QBXML parse errors.
            // To prevent data loss, we prepend the Name value to the writable Memo field.
            PreserveNameToMemo(source, transformed, entityType);

            // ── Fix #14 (Revised): Zero out Notes field for Employees ────────
            // Historical data (RAISES AND PROMOTIONS HISTORY blocks) is not
            // needed in the migration and was causing QBXML parse errors for
            // 10 employees. Rather than truncating, simply remove the field.
            ZeroOutEmployeeNotes(transformed, entityType);

            // Ensure class assignments are preserved in transactions
            if (_transformationRules.PreserveClassTracking && ClassTrackingHeaderTypes.Contains(entityType))
            {
                PreserveClassAssignment(source, transformed);
            }

            _transformationReport.TotalEntitiesTransformed++;

            return transformed;
        }

        /// <summary>
        /// Removes fields that only exist in SDK 16.0 (QB 2023) and are not supported in SDK 15.0 (QB 2021).
        /// These fields cause 0x80040400 COM errors when sent to QB 2021.
        /// </summary>
        private void RemoveSDK16OnlyFields(JObject fields, string context)
        {
            var fieldsToRemove = new List<string>();

            foreach (var prop in fields.Properties())
            {
                if (QBSDKVersionHelper.SDK16OnlyFields.Contains(prop.Name))
                {
                    fieldsToRemove.Add(prop.Name);
                }
                else if (prop.Value is JObject nested)
                {
                    // Check nested fields (e.g., "EmployeePayrollInfo.ClearEarnings")
                    RemoveSDK16OnlyFields(nested, $"{context}.{prop.Name}");
                }
            }

            foreach (var fieldName in fieldsToRemove)
            {
                fields.Remove(fieldName);
                _sdk16FieldsRemoved++;
                Log.Debug("  Removed SDK 16.0-only field '{Field}' from {Context} (not supported in QB 2021)",
                    fieldName, context);
            }
        }

        /// <summary>
        /// Enforces QB 2021 (SDK 15.0) field length limits.
        /// Fields that are longer than QB 2021 allows are truncated.
        /// </summary>
        private void EnforceQB2021FieldLengths(JObject fields, string context)
        {
            foreach (var prop in fields.Properties().ToList())
            {
                if (prop.Value is JObject nested)
                {
                    EnforceQB2021FieldLengths(nested, $"{context}.{prop.Name}");
                    continue;
                }

                if (prop.Value.Type != JTokenType.String) continue;

                var strValue = prop.Value.ToString();
                var maxLen = QBSDKVersionHelper.GetQB2021MaxLength(prop.Name);

                if (maxLen.HasValue && strValue.Length > maxLen.Value)
                {
                    var truncated = strValue.Substring(0, maxLen.Value);
                    fields[prop.Name] = truncated;
                    _fieldsLengthAdjusted++;
                    Log.Debug("  Truncated field '{Field}' from {Original} to {Max} chars for QB 2021 in {Context}",
                        prop.Name, strValue.Length, maxLen.Value, context);
                }
            }
        }

        /// <summary>
        /// Fixes empty Name fields by generating a placeholder name.
        /// Empty names cause QB import to fail with validation errors.
        /// </summary>
        private void FixEmptyNameField(QBEntity entity, string entityType)
        {
            if (string.IsNullOrWhiteSpace(entity.Name) && string.IsNullOrWhiteSpace(entity.FullName))
            {
                // Generate a name from available fields
                var generatedName = GenerateEntityName(entity, entityType);
                entity.Name = generatedName;
                entity.FullName = generatedName;
                entity.Fields["Name"] = generatedName;
                _emptyNameFieldsFixed++;
                Log.Warning("  Fixed empty Name for {EntityType}: generated '{GeneratedName}'",
                    entityType, generatedName);
            }
            else if (string.IsNullOrWhiteSpace(entity.Name) && !string.IsNullOrWhiteSpace(entity.FullName))
            {
                entity.Name = entity.FullName;
                entity.Fields["Name"] = entity.FullName;
                _emptyNameFieldsFixed++;
                Log.Debug("  Copied FullName to empty Name for {EntityType}: '{Name}'",
                    entityType, entity.FullName);
            }
        }

        /// <summary>
        /// Generates a fallback entity name from available fields.
        /// </summary>
        private static string GenerateEntityName(QBEntity entity, string entityType)
        {
            // Try common name-like fields
            var candidates = new[] { "CompanyName", "FirstName", "Description", "RefNumber", "TxnNumber", "Memo" };
            foreach (var field in candidates)
            {
                var value = entity.Fields[field]?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    // Truncate to 41 chars (QB 2021 Name limit)
                    return value.Length > 41 ? value.Substring(0, 41) : value;
                }
            }

            // Try combining first + last name
            var first = entity.Fields["FirstName"]?.ToString() ?? "";
            var last = entity.Fields["LastName"]?.ToString() ?? "";
            if (!string.IsNullOrWhiteSpace(first) || !string.IsNullOrWhiteSpace(last))
            {
                var combined = $"{first} {last}".Trim();
                return combined.Length > 41 ? combined.Substring(0, 41) : combined;
            }

            // Last resort: use entity type + ID
            var id = entity.ListID ?? entity.TxnID ?? Guid.NewGuid().ToString("N").Substring(0, 8);
            return $"{entityType}_{id}".Substring(0, Math.Min(41, $"{entityType}_{id}".Length));
        }

        /// <summary>
        /// Simplifies payroll-related fields for QB 2021 compatibility.
        /// QB 2023 has a more complex payroll structure not supported by QB 2021's SDK.
        /// </summary>
        private void SimplifyPayrollFields(JObject fields, string context)
        {
            var fieldsToRemove = new List<string>();

            foreach (var prop in fields.Properties())
            {
                if (QBSDKVersionHelper.IsPayrollFieldToSimplify(prop.Name))
                {
                    fieldsToRemove.Add(prop.Name);
                }
            }

            foreach (var fieldName in fieldsToRemove)
            {
                fields.Remove(fieldName);
                _payrollFieldsSimplified++;
                Log.Debug("  Simplified payroll field '{Field}' from {Context} for QB 2021 compatibility",
                    fieldName, context);
            }
        }

        /// <summary>
        /// Fixes credit card balance sign inversion issue flagged by accountant.
        /// QB 2023 may report CC balances with opposite sign convention from QB 2021.
        /// In QB 2021, CC (Credit Card type) account balances should be positive when you owe money.
        /// </summary>
        private void FixCCBalanceSigns(QBEntity entity, string entityType)
        {
            if (entityType != "Accounts") return;

            var accountType = entity.Fields["AccountType"]?.ToString();
            if (string.IsNullOrEmpty(accountType) ||
                !accountType.Equals("CreditCard", StringComparison.OrdinalIgnoreCase))
                return;

            // Check for balance fields that may need sign inversion
            var balanceFields = new[] { "Balance", "TotalBalance", "OpenBalance" };
            foreach (var balField in balanceFields)
            {
                var balValue = entity.Fields[balField]?.ToString();
                if (!string.IsNullOrEmpty(balValue) && decimal.TryParse(balValue,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var balance))
                {
                    // QB 2021 expects positive balance = money owed on CC
                    // QB 2023 sometimes reports with inverted sign
                    // We negate negative CC balances to match QB 2021 convention
                    if (balance < 0)
                    {
                        var corrected = Math.Abs(balance);
                        entity.Fields[balField] = corrected.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                        _ccBalanceSignsFixed++;
                        Log.Information("  Fixed CC balance sign for '{Name}': {Original} → {Corrected}",
                            entity.Name, balValue, corrected.ToString("F2"));
                    }
                }
            }
        }

        /// <summary>
        /// Ensures account numbers from QB 2023 are preserved in the transformed data.
        /// Accountant flagged that account numbers were missing after migration.
        /// </summary>
        private void PreserveAccountNumbers(QBEntity source, QBEntity transformed, string entityType)
        {
            if (entityType != "Accounts") return;

            var sourceAcctNum = source.Fields["AccountNumber"]?.ToString();
            var transformedAcctNum = transformed.Fields["AccountNumber"]?.ToString();

            if (!string.IsNullOrEmpty(sourceAcctNum) && string.IsNullOrEmpty(transformedAcctNum))
            {
                // Account number was lost during transformation — restore it
                transformed.Fields["AccountNumber"] = sourceAcctNum;
                Log.Debug("  Restored AccountNumber '{AcctNum}' for account '{Name}'",
                    sourceAcctNum, source.Name);
            }
        }

        /// <summary>
        /// Preserves transaction "Name" field data by mapping it into the "Memo" field.
        /// 
        /// Background: Fix #8 excludes the read-only "Name" field from QBXML Add requests
        /// to avoid parse errors (Name is system-assigned in QB transactions). However, the
        /// exported Name often contains meaningful data (e.g., "ADJ - MH", vendor names, 
        /// reference codes) that would otherwise be lost.
        /// 
        /// Strategy: Prepend the Name value to the Memo field using the format:
        ///   [Imported Name: {name}] {existing_memo}
        /// This preserves the data in a searchable, writable field without breaking QBXML.
        /// 
        /// Applies to: Checks, Deposits, SalesReceipts, JournalEntries
        /// Skips: Transfers (no Memo field — already at 100% success rate)
        /// </summary>
        private void PreserveNameToMemo(QBEntity source, QBEntity transformed, string entityType)
        {
            // Only applies to transaction types that have both Name and Memo fields
            if (!NameToMemoTransactionTypes.Contains(entityType))
                return;

            // Get the Name value from the SOURCE entity (before any transformation removed it)
            var nameValue = source.Fields["Name"]?.ToString()
                ?? source.Name;

            // Skip if there's no Name data to preserve
            if (string.IsNullOrWhiteSpace(nameValue))
                return;

            // Build the prefix tag
            var nameTag = $"[Imported Name: {nameValue.Trim()}]";

            // Get the existing Memo from the TRANSFORMED entity (preserve any existing memo data)
            var existingMemo = transformed.Fields["Memo"]?.ToString()?.Trim();

            // Combine: prepend Name tag to existing Memo, or use Name tag alone
            string newMemo;
            if (!string.IsNullOrWhiteSpace(existingMemo))
            {
                newMemo = $"{nameTag} {existingMemo}";
            }
            else
            {
                newMemo = nameTag;
            }

            // QB 2021 Memo field max length is 4095 characters — enforce limit
            const int memoMaxLength = 4095;
            if (newMemo.Length > memoMaxLength)
            {
                newMemo = newMemo.Substring(0, memoMaxLength);
                Log.Debug("  Name→Memo: Truncated combined memo to {Max} chars for {EntityType}",
                    memoMaxLength, entityType);
            }

            // Apply the updated Memo to the transformed entity
            transformed.Fields["Memo"] = newMemo;

            _nameToMemoPreserved++;
            Log.Debug("  Name→Memo: Preserved '{Name}' into Memo for {EntityType} (TxnID={TxnID})",
                nameValue, entityType, source.TxnID ?? "N/A");
        }

        /// <summary>
        /// FIX #38: Converts CreditCardCharge/CreditCardCredit transactions to JournalEntry format.
        /// 
        /// QB 2021 does not support CreditCardCharge or CreditCardCredit transaction types.
        /// However, "the journal is the truth" - every transaction is ultimately just debits and credits.
        /// This method converts credit card transactions to journal entries, preserving the accounting
        /// integrity while ensuring QB 2021 compatibility.
        /// 
        /// Conversion Logic:
        /// - CreditCardCharge:
        ///   * Original: Debit expense accounts, Credit credit card liability account
        ///   * Journal: Same - convert expense lines to debit journal lines, add credit line for CC account
        /// 
        /// - CreditCardCredit (refund):
        ///   * Original: Debit credit card account, Credit expense/income accounts
        ///   * Journal: Same - CC account becomes debit line, expense lines become credit lines
        /// 
        /// Preserved Fields:
        /// - TxnDate, RefNumber, Memo (with "[From CreditCard...]" prefix for audit trail)
        /// - All line item account references and amounts
        /// - TxnID preserved in Memo for reference tracking
        /// 
        /// ═══════════════════════════════════════════════════════════════════
        /// Fix #38: Convert CreditCardCharge/CreditCardCredit to JournalEntry
        /// ═══════════════════════════════════════════════════════════════════
        /// PROBLEM: QB 2021 SDK 15.0 does not support CreditCardCharge or
        /// CreditCardCredit transaction types (confirmed via schema extraction).
        /// These types were added in later QB versions but don't exist in QB 2021.
        /// 
        /// SOLUTION: Convert to JournalEntry format during transformation phase.
        /// As Joseph said: "the journal is the truth" - every transaction is
        /// ultimately debits and credits. This conversion preserves accounting
        /// integrity while maintaining QB 2021 compatibility.
        /// 
        /// ACCOUNTING LOGIC (Double-Entry Bookkeeping):
        /// 
        /// CreditCardCharge (Purchase on credit card):
        ///   - Debit: Expense accounts (increases expenses)
        ///   - Credit: Credit Card liability account (increases debt owed)
        ///   Example: $100 office supplies purchase
        ///     DR Office Supplies $100
        ///     CR Visa Credit Card $100
        /// 
        /// CreditCardCredit (Refund/return):
        ///   - Credit: Expense accounts (reduces expenses)
        ///   - Debit: Credit Card liability account (reduces debt owed)
        ///   Example: $50 return of office supplies
        ///     DR Visa Credit Card $50
        ///     CR Office Supplies $50
        /// 
        /// IMPACT: 378 credit card transactions (369 charges + 9 credits) from
        /// Joshs_Gold_Coast_II_2023.qbw now migrate successfully to QB 2021.
        /// Expected to resolve $400K balance discrepancy (Educators Credit Union
        /// and Cash Register Sales accounts).
        /// ═══════════════════════════════════════════════════════════════════
        /// </summary>
        private QBEntity ConvertCreditCardToJournalEntry(QBEntity creditCardTxn, string originalType)
        {
            bool isCreditCardCharge = originalType.Equals("CreditCardCharges", StringComparison.OrdinalIgnoreCase);
            string txnPrefix = isCreditCardCharge ? "[From CreditCardCharge]" : "[From CreditCardCredit]";

            var journalEntry = new QBEntity
            {
                EntityType = "JournalEntry",
                TxnID = creditCardTxn.TxnID,  // Preserve original TxnID for reference
                Name = creditCardTxn.Name,
                FullName = creditCardTxn.FullName,
                IsActive = true,
                ExportedAt = creditCardTxn.ExportedAt,
                Fields = new JObject(),
                LineItems = new List<JObject>()
            };

            // Copy header fields
            var headerFieldsToCopy = new[] { "TxnDate", "RefNumber", "IsAdjustment" };
            foreach (var field in headerFieldsToCopy)
            {
                if (creditCardTxn.Fields.ContainsKey(field))
                {
                    journalEntry.Fields[field] = creditCardTxn.Fields[field]?.DeepClone();
                }
            }

            // Build Memo with conversion info and original TxnID
            var originalMemo = creditCardTxn.Fields["Memo"]?.ToString()?.Trim();
            var txnIdNote = !string.IsNullOrEmpty(creditCardTxn.TxnID) ? $" TxnID:{creditCardTxn.TxnID}" : "";
            string newMemo;
            if (!string.IsNullOrWhiteSpace(originalMemo))
            {
                newMemo = $"{txnPrefix}{txnIdNote} {originalMemo}";
            }
            else
            {
                newMemo = $"{txnPrefix}{txnIdNote}";
            }
            journalEntry.Fields["Memo"] = newMemo.Length > 4095 ? newMemo.Substring(0, 4095) : newMemo;

            // Get the credit card account reference from the header
            var ccAccountRef = creditCardTxn.Fields["AccountRef"]?.DeepClone() as JObject;
            var ccAccountName = ccAccountRef?["FullName"]?.ToString() ?? "Credit Card";

            // Get total amount
            var totalAmount = decimal.Parse(creditCardTxn.Fields["Amount"]?.ToString() ?? "0",
                System.Globalization.CultureInfo.InvariantCulture);

            // Convert line items
            // CreditCardCharge lines (ExpenseLineRet, ItemLineRet) become DEBIT journal lines
            // CreditCardCredit lines become CREDIT journal lines
            foreach (var line in creditCardTxn.LineItems)
            {
                var journalLine = new JObject();

                // Determine if this is debit or credit based on transaction type
                if (isCreditCardCharge)
                {
                    // Charge: expense lines are debits
                    journalLine["DebitAmount"] = line["Amount"]?.DeepClone();
                }
                else
                {
                    // Credit/refund: expense lines are credits
                    journalLine["CreditAmount"] = line["Amount"]?.DeepClone();
                }

                // Copy account reference
                if (line.ContainsKey("AccountRef"))
                {
                    journalLine["AccountRef"] = line["AccountRef"]?.DeepClone();
                }

                // Copy other relevant fields
                if (line.ContainsKey("Memo"))
                {
                    journalLine["Memo"] = line["Memo"]?.DeepClone();
                }
                if (line.ContainsKey("ClassRef"))
                {
                    journalEntry.Fields["ClassRef"] = line["ClassRef"]?.DeepClone();  // Move to header
                }

                journalEntry.LineItems.Add(journalLine);
            }

            // Add offsetting line for the credit card account
            var ccLine = new JObject();
            ccLine["AccountRef"] = ccAccountRef;
            ccLine["Memo"] = $"Credit card transaction";

            if (isCreditCardCharge)
            {
                // Charge: CC account is credited (increases liability)
                ccLine["CreditAmount"] = totalAmount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            }
            else
            {
                // Credit/refund: CC account is debited (reduces liability)
                ccLine["DebitAmount"] = totalAmount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            }

            journalEntry.LineItems.Add(ccLine);

            Log.Debug("  Converted {Type} (TxnID={TxnID}, Amount={Amount}) to JournalEntry with {Lines} lines",
                originalType, creditCardTxn.TxnID ?? "N/A", totalAmount, journalEntry.LineItems.Count);

            return journalEntry;
        }

        // ═══════════════════════════════════════════════════════════════════
        // Fix #14 (Revised): Zero out Notes field - historical data not
        // needed in migration
        // ═══════════════════════════════════════════════════════════════════
        // 10 Employees were failing import because their <Notes> field
        // contained massive "RAISES AND PROMOTIONS HISTORY FROM EMPLOYEE
        // ORGANIZER" blocks with compensation history, formatted text, and
        // special characters that broke QBXML parsing.
        //
        // Original approach: truncate to 4,000 chars + sanitize XML chars.
        // Revised approach (per Joseph): simply zero out the Notes field
        // entirely for Employees. The historical payroll/promotion data is
        // not needed in the migrated QB 2021 file — it's archival data
        // that lives in the source QB 2023 file.
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Fix #14 (Revised): Zero out Notes field for Employees.
        /// Historical data (raises, promotions, compensation history) is not
        /// needed in the migration and was causing QBXML parse errors.
        /// Only applies to Employees — other entity types keep their Notes.
        /// </summary>
        private void ZeroOutEmployeeNotes(QBEntity entity, string entityType)
        {
            // Only zero out Notes for Employees
            if (!entityType.Equals("Employees", StringComparison.OrdinalIgnoreCase))
                return;

            var notesToken = entity.Fields["Notes"];
            if (notesToken == null || notesToken.Type == JTokenType.Null)
                return;

            var originalNotes = notesToken.ToString();
            if (string.IsNullOrWhiteSpace(originalNotes))
                return;

            // Remove the Notes field entirely from the entity
            entity.Fields.Remove("Notes");
            _employeeNotesTruncated++;

            Log.Information("  Fix #14: Zeroed out Notes for Employee '{Name}' " +
                "(was {Len} chars — historical data not needed in migration)",
                entity.Name ?? entity.FullName ?? "unknown",
                originalNotes.Length);
        }

        /// <summary>
        /// FIX #1: Parses hierarchical names (colon-separated) into leaf + parent components.
        /// Stores the parsed components in the entity's Fields JSON for easier import.
        /// Entity types that support hierarchy: Accounts, Customers, Vendors, Items, Classes.
        /// Example: "Expenses:Office:Supplies" → Name="Supplies", _parentFullName="Expenses:Office"
        /// </summary>
        private void ParseHierarchicalName(QBEntity entity, string entityType)
        {
            var hierarchicalTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Accounts", "Customers", "Vendors", "Items", "Classes"
            };

            if (!hierarchicalTypes.Contains(entityType)) return;

            var fullName = entity.FullName ?? entity.Fields["FullName"]?.ToString()
                ?? entity.Name ?? entity.Fields["Name"]?.ToString();

            if (string.IsNullOrEmpty(fullName) || !fullName.Contains(':')) return;

            var parts = fullName.Split(':');
            var leafName = parts[parts.Length - 1].Trim();
            var parentFullName = string.Join(":", parts.Take(parts.Length - 1)).Trim();

            if (!string.IsNullOrEmpty(parentFullName))
            {
                // Store the parsed hierarchy in the transformed JSON
                // The importer will use these to construct proper QBXML
                entity.Fields["_leafName"] = leafName;
                entity.Fields["_parentFullName"] = parentFullName;
                entity.Fields["_isHierarchical"] = true;

                // The Name field should contain the leaf name for QB Add requests
                // (FullName is read-only / excluded from Add)
                entity.Name = leafName;
                entity.Fields["Name"] = leafName;

                _hierarchicalNamesParsed++;
                Log.Debug("  FIX #1: Parsed hierarchical name for {EntityType}: " +
                    "'{FullName}' → leaf='{Leaf}', parent='{Parent}'",
                    entityType, fullName, leafName, parentFullName);
            }
        }

        /// <summary>
        /// FIX #2: Forces IsActive = true for ALL entities during transformation.
        /// Accountant requirement: inactive entities must be imported as active because
        /// QB 2021 won't allow references to inactive items during import.
        /// Applies to: Customers, Vendors, Employees, Items, Accounts.
        /// </summary>
        private void ForceIsActive(QBEntity entity, string entityType)
        {
            if (!ActiveStatusEntityTypes.Contains(entityType)) return;

            var wasActive = entity.IsActive;
            entity.IsActive = true;
            entity.Fields["IsActive"] = JToken.FromObject("true");

            if (!wasActive)
            {
                _isActiveOverrides++;
                Log.Debug("  FIX #2: Forced IsActive=true for {EntityType} '{Name}' (was inactive)",
                    entityType, entity.Name);
            }
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
        /// Now includes format-aware processing for dates, currencies, phone numbers, and other field types.
        /// </summary>
        private JToken ProcessValue(JToken value, FieldMapping mapping)
        {
            var strValue = value.ToString();

            // ── Format-Aware Processing ──────────────────────────────────
            // Determine the format type: explicit from mapping, or auto-detected from field name
            var formatType = mapping.FormatType
                ?? DetectFormatType(mapping.SourceField, strValue);

            if (formatType != null)
            {
                var formatResult = ApplyFormatPreservation(strValue, formatType, mapping);
                if (formatResult != null)
                {
                    return JToken.FromObject(formatResult);
                }
            }

            // ── Standard Truncation (non-formatted fields) ───────────────
            if (mapping.MaxLength.HasValue && _globalSettings.TruncateLongStrings
                && strValue.Length > mapping.MaxLength.Value)
            {
                // Use format-aware truncation if configured
                if (_formatRules.TruncationBehavior == "PreserveFormat" && formatType != null)
                {
                    strValue = TransformFunctions.FormatAwareTruncate(
                        strValue, mapping.MaxLength.Value, formatType);
                    _transformationReport.FormatStats.FormatAwareTruncations++;
                }
                else
                {
                    Log.Debug("Truncating field {Field} from {Original} to {Max} chars",
                        mapping.SourceField, strValue.Length, mapping.MaxLength.Value);
                    strValue = strValue.Substring(0, mapping.MaxLength.Value);
                }
                _transformationReport.TotalFieldsTruncated++;
                return JToken.FromObject(strValue);
            }

            return value.DeepClone();
        }

        /// <summary>
        /// Auto-detects the format type of a field based on its name and value.
        /// Returns: "date", "currency", "phone", "postalcode", "memo", or null.
        /// </summary>
        private string? DetectFormatType(string fieldName, string value)
        {
            if (string.IsNullOrEmpty(fieldName)) return null;

            // Check date fields
            if (_formatRules.PreserveDateFormat &&
                TransformFunctions.IsDateField(fieldName, _formatRules.DateFieldPatterns))
            {
                return "date";
            }

            // Check currency fields
            if (_formatRules.PreserveCurrencyFormat &&
                TransformFunctions.IsCurrencyField(fieldName, _formatRules.CurrencyFieldPatterns))
            {
                return "currency";
            }

            // Check phone fields by name
            var leafField = fieldName.Contains('.') ? fieldName.Split('.').Last() : fieldName;
            if (_formatRules.PreservePhoneFormat &&
                (leafField.Equals("Phone", StringComparison.OrdinalIgnoreCase) ||
                 leafField.Equals("AltPhone", StringComparison.OrdinalIgnoreCase) ||
                 leafField.Equals("Fax", StringComparison.OrdinalIgnoreCase) ||
                 leafField.Equals("Mobile", StringComparison.OrdinalIgnoreCase)))
            {
                return "phone";
            }

            // Check postal code fields
            if (_formatRules.PreservePostalCodeFormat &&
                leafField.Equals("PostalCode", StringComparison.OrdinalIgnoreCase))
            {
                return "postalcode";
            }

            // Check memo/notes fields
            if (_formatRules.PreserveMemoFormatting &&
                (leafField.Equals("Memo", StringComparison.OrdinalIgnoreCase) ||
                 leafField.Equals("Notes", StringComparison.OrdinalIgnoreCase)))
            {
                return "memo";
            }

            return null;
        }

        /// <summary>
        /// Applies format-specific preservation logic to a field value.
        /// Returns the preserved value string, or null if no format processing was applied.
        /// </summary>
        private string? ApplyFormatPreservation(string value, string formatType, FieldMapping mapping)
        {
            FormatPreservationResult? result = null;

            switch (formatType.ToLowerInvariant())
            {
                case "date":
                    result = TransformFunctions.PreserveDate(
                        value,
                        _formatRules.DateFormatStandard,
                        _formatRules.StripTimezoneFromDates);

                    _transformationReport.FormatStats.DateFieldsProcessed++;
                    if (result.WasPreserved)
                    {
                        _transformationReport.FormatStats.DateFormatsPreserved++;
                        if (result.FormatAction == "StripTimezone")
                            _transformationReport.FormatStats.DateTimezonesStripped++;
                    }
                    else
                    {
                        _transformationReport.FormatStats.DateFormatIssues++;
                    }

                    // Track date format breakdown
                    if (result.DetectedFormat != null)
                    {
                        var fmt = result.DetectedFormat;
                        if (!_transformationReport.FormatStats.DateFormatBreakdown.ContainsKey(fmt))
                            _transformationReport.FormatStats.DateFormatBreakdown[fmt] = 0;
                        _transformationReport.FormatStats.DateFormatBreakdown[fmt]++;
                    }

                    if (!string.IsNullOrEmpty(result.Issue))
                    {
                        _transformationReport.FormatStats.FormatIssues.Add(
                            $"[{mapping.SourceField}] {result.Issue}: '{value}'");
                    }

                    Log.Debug("Date format preserved: {Field} '{Original}' -> '{Preserved}' ({Action})",
                        mapping.SourceField, value, result.PreservedValue, result.FormatAction);
                    break;

                case "currency":
                    result = TransformFunctions.PreserveCurrencyFormat(
                        value, _formatRules.CurrencyDecimalPlaces);

                    _transformationReport.FormatStats.CurrencyFieldsProcessed++;
                    if (result.WasPreserved)
                        _transformationReport.FormatStats.CurrencyFormatsPreserved++;
                    break;

                case "phone":
                    var phoneMax = mapping.MaxLength ?? 21;
                    result = TransformFunctions.PreservePhoneFormat(value, phoneMax);

                    _transformationReport.FormatStats.PhoneFieldsProcessed++;
                    if (result.WasPreserved)
                        _transformationReport.FormatStats.PhoneFormatsPreserved++;
                    break;

                case "postalcode":
                    var postalMax = mapping.MaxLength ?? 13;
                    result = TransformFunctions.PreservePostalCodeFormat(value, postalMax);

                    _transformationReport.FormatStats.PostalCodeFieldsProcessed++;
                    if (result.WasPreserved)
                        _transformationReport.FormatStats.PostalCodeFormatsPreserved++;
                    break;

                case "memo":
                    var memoMax = mapping.MaxLength ?? 4095;
                    result = TransformFunctions.PreserveMemoFormat(
                        value, memoMax, _formatRules.PreserveEncoding);

                    _transformationReport.FormatStats.MemoFieldsProcessed++;
                    if (_formatRules.PreserveEncoding)
                        _transformationReport.FormatStats.EncodingPreserved++;
                    break;
            }

            if (result != null && result.WasPreserved)
            {
                if (!string.IsNullOrEmpty(result.Issue))
                {
                    _transformationReport.FormatStats.FormatIssues.Add(
                        $"[{mapping.SourceField}] {result.Issue}");
                }
                return result.PreservedValue;
            }

            return null;
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

            // Format preservation summary
            var fs = _transformationReport.FormatStats;
            if (fs.DateFieldsProcessed > 0)
                parts.Add($"Dates preserved: {fs.DateFormatsPreserved}/{fs.DateFieldsProcessed}");
            if (fs.CurrencyFieldsProcessed > 0)
                parts.Add($"Currency preserved: {fs.CurrencyFormatsPreserved}/{fs.CurrencyFieldsProcessed}");
            if (fs.FormatIssues.Count > 0)
                parts.Add($"Format issues: {fs.FormatIssues.Count}");

            _transformationReport.Summary = string.Join(" | ", parts);
        }

        /// <summary>
        /// Logs a summary of format preservation statistics.
        /// </summary>
        private void LogFormatPreservationSummary()
        {
            var fs = _transformationReport.FormatStats;
            var totalFormatted = fs.DateFieldsProcessed + fs.CurrencyFieldsProcessed +
                fs.PhoneFieldsProcessed + fs.PostalCodeFieldsProcessed + fs.MemoFieldsProcessed;

            if (totalFormatted == 0) return;

            Log.Information("────────────────────────────────────────────");
            Log.Information("  FORMAT PRESERVATION SUMMARY");
            Log.Information("────────────────────────────────────────────");

            if (fs.DateFieldsProcessed > 0)
            {
                Log.Information("    Date fields: {Processed} processed, {Preserved} preserved, {Issues} issues",
                    fs.DateFieldsProcessed, fs.DateFormatsPreserved, fs.DateFormatIssues);
                if (fs.DateTimezonesStripped > 0)
                    Log.Information("      Timezone stripped: {Count} datetime fields", fs.DateTimezonesStripped);
                foreach (var (fmt, count) in fs.DateFormatBreakdown.OrderByDescending(kv => kv.Value))
                    Log.Information("      Format '{Format}': {Count} fields", fmt, count);
            }

            if (fs.CurrencyFieldsProcessed > 0)
                Log.Information("    Currency fields: {Processed} processed, {Preserved} preserved",
                    fs.CurrencyFieldsProcessed, fs.CurrencyFormatsPreserved);

            if (fs.PhoneFieldsProcessed > 0)
                Log.Information("    Phone fields: {Processed} processed, {Preserved} preserved",
                    fs.PhoneFieldsProcessed, fs.PhoneFormatsPreserved);

            if (fs.PostalCodeFieldsProcessed > 0)
                Log.Information("    Postal code fields: {Processed} processed, {Preserved} preserved",
                    fs.PostalCodeFieldsProcessed, fs.PostalCodeFormatsPreserved);

            if (fs.MemoFieldsProcessed > 0)
                Log.Information("    Memo/Notes fields: {Processed} processed", fs.MemoFieldsProcessed);

            if (fs.FormatAwareTruncations > 0)
                Log.Information("    Format-aware truncations: {Count}", fs.FormatAwareTruncations);

            if (fs.EncodingPreserved > 0)
                Log.Information("    Encoding preserved: {Count} fields", fs.EncodingPreserved);

            if (fs.FormatIssues.Count > 0)
            {
                Log.Warning("    Format issues ({Count}):", fs.FormatIssues.Count);
                foreach (var issue in fs.FormatIssues.Take(10))
                    Log.Warning("      {Issue}", issue);
                if (fs.FormatIssues.Count > 10)
                    Log.Warning("      ... and {More} more issues", fs.FormatIssues.Count - 10);
            }
        }

        /// <summary>
        /// Logs a summary of QB 2021 SDK 15.0 compatibility adjustments made during transformation.
        /// </summary>
        private void LogQB2021CompatibilitySummary()
        {
            var totalAdjustments = _sdk16FieldsRemoved + _fieldsLengthAdjusted +
                _emptyNameFieldsFixed + _payrollFieldsSimplified + _ccBalanceSignsFixed +
                _hierarchicalNamesParsed + _isActiveOverrides + _nameToMemoPreserved +
                _employeeNotesTruncated;

            if (totalAdjustments == 0) return;

            Log.Information("────────────────────────────────────────────");
            Log.Information("  QB 2021 (SDK 15.0) COMPATIBILITY SUMMARY");
            Log.Information("────────────────────────────────────────────");

            if (_hierarchicalNamesParsed > 0)
                Log.Information("    FIX #1: Hierarchical names parsed: {Count} (leaf + ParentRef)", _hierarchicalNamesParsed);
            if (_isActiveOverrides > 0)
                Log.Information("    FIX #2: IsActive forced to true:   {Count} (accountant requirement)", _isActiveOverrides);
            if (_sdk16FieldsRemoved > 0)
                Log.Information("    SDK 16.0-only fields removed:      {Count} (would cause 0x80040400)", _sdk16FieldsRemoved);
            if (_fieldsLengthAdjusted > 0)
                Log.Information("    Fields truncated for 2021 limits:  {Count}", _fieldsLengthAdjusted);
            if (_emptyNameFieldsFixed > 0)
                Log.Information("    Empty Name fields fixed:           {Count}", _emptyNameFieldsFixed);
            if (_payrollFieldsSimplified > 0)
                Log.Information("    Payroll fields simplified:         {Count}", _payrollFieldsSimplified);
            if (_ccBalanceSignsFixed > 0)
                Log.Information("    CC balance signs corrected:        {Count}", _ccBalanceSignsFixed);
            if (_nameToMemoPreserved > 0)
                Log.Information("    Name→Memo preserved:               {Count} (transaction names saved to Memo)", _nameToMemoPreserved);
            if (_employeeNotesTruncated > 0)
                Log.Information("    Fix #14 Employee Notes zeroed:     {Count} (historical data not needed)", _employeeNotesTruncated);

            Log.Information("    ────────────────────────────────");
            Log.Information("    TOTAL compatibility adjustments: {Total}", totalAdjustments);
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
