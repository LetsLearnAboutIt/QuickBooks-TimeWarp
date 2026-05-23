using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using QB_TimeWarp.Helpers;
using QB_TimeWarp.Models;
using Serilog;

namespace QB_TimeWarp.Services
{
    /// <summary>
    /// Exports all entity data from QuickBooks via QBXML SDK.
    /// Automatically adapts QBXML version based on detected QB SDK version.
    /// Data is serialized to JSON files organized by entity type.
    /// </summary>
    public class DataExporter
    {
        private readonly QBConnectionManager _connection;
        private readonly ExportConfig _exportConfig;
        private readonly string _outputDirectory;
        private readonly string _sdkVersion;
        private string _effectiveSDKVersion; // May be adjusted after version detection
        private string? _detectedMaxSDKVersion;

        /// <summary>
        /// Maps our entity type names to QBXML query/response type names.
        /// </summary>
        private static readonly Dictionary<string, (string QueryType, string ResponseType)> QueryMap = new()
        {
            // Lists
            ["Accounts"]        = ("AccountQuery",         "AccountRet"),
            ["Customers"]       = ("CustomerQuery",        "CustomerRet"),
            ["Vendors"]         = ("VendorQuery",          "VendorRet"),
            ["Employees"]       = ("EmployeeQuery",        "EmployeeRet"),
            ["Items"]           = ("ItemQuery",            "ItemRet"),         // Generic item query returns all types
            // ── FIX #9: ItemSalesTax must be queried explicitly ───────────────
            // The generic ItemQuery returns ItemServiceRet / ItemInventoryRet /
            // etc., but NOT ItemSalesTaxRet. Sales-tax items (e.g. "WI SALES
            // & EXPO") are referenced by Customer.ItemSalesTaxRef and
            // SalesReceipt.ItemSalesTaxRef yet were missing from every export.
            // Adding a dedicated ItemSalesTaxQuery surfaces them so the
            // dependency analyzer and Stage 1 foundation importer can see
            // and recreate them in QB 2021.
            ["ItemSalesTax"]    = ("ItemSalesTaxQuery",    "ItemSalesTaxRet"),
            ["PaymentMethods"]  = ("PaymentMethodQuery",   "PaymentMethodRet"),
            ["Terms"]           = ("TermsQuery",           "StandardTermsRet"),
            ["Classes"]         = ("ClassQuery",           "ClassRet"),
            ["SalesTaxCodes"]   = ("SalesTaxCodeQuery",    "SalesTaxCodeRet"),
            ["ShipMethods"]     = ("ShipMethodQuery",      "ShipMethodRet"),
            ["CustomerTypes"]   = ("CustomerTypeQuery",    "CustomerTypeRet"),
            ["VendorTypes"]     = ("VendorTypeQuery",      "VendorTypeRet"),
            ["JobTypes"]        = ("JobTypeQuery",         "JobTypeRet"),
            ["PriceLevels"]     = ("PriceLevelQuery",      "PriceLevelRet"),
            // FIX #10: CustomerMsgs as a first-class export type.
            //          Invoice/SalesReceipt/CreditMemo records carry a CustomerMsgRef
            //          (e.g. "Thank you for your business!"). Without an export of the
            //          CustomerMsg list, the destination QB has no record of those
            //          messages and importing the transactions fails with Error 3140.
            ["CustomerMsgs"]    = ("CustomerMsgQuery",     "CustomerMsgRet"),

            // Transactions
            ["Invoices"]        = ("InvoiceQuery",         "InvoiceRet"),
            ["Bills"]           = ("BillQuery",            "BillRet"),
            ["Payments"]        = ("ReceivePaymentQuery",  "ReceivePaymentRet"),
            ["SalesReceipts"]   = ("SalesReceiptQuery",    "SalesReceiptRet"),
            ["PurchaseOrders"]  = ("PurchaseOrderQuery",   "PurchaseOrderRet"),
            ["JournalEntries"]  = ("JournalEntryQuery",    "JournalEntryRet"),
            ["CreditMemos"]     = ("CreditMemoQuery",      "CreditMemoRet"),
            ["Estimates"]       = ("EstimateQuery",        "EstimateRet"),
            ["Deposits"]        = ("DepositQuery",         "DepositRet"),
            ["Checks"]          = ("CheckQuery",           "CheckRet"),
            ["VendorCredits"]   = ("VendorCreditQuery",    "VendorCreditRet"),
            ["InventoryAdjustments"] = ("InventoryAdjustmentQuery", "InventoryAdjustmentRet"),
            ["Transfers"]       = ("TransferQuery",        "TransferRet"),

            // Settings
            ["Preferences"]     = ("PreferencesQuery",     "PreferencesRet"),
            ["CompanyInfo"]     = ("CompanyQuery",         "CompanyRet"),
        };

        /// <summary>
        /// Transaction entity types that support date range filtering.
        /// </summary>
        private static readonly HashSet<string> TransactionTypes = new()
        {
            "Invoices", "Bills", "Payments", "SalesReceipts", "PurchaseOrders",
            "JournalEntries", "CreditMemos", "Estimates", "Deposits", "Checks",
            "VendorCredits", "InventoryAdjustments", "Transfers"
        };

        /// <summary>
        /// Known line item element names within transaction responses.
        /// </summary>
        private static readonly Dictionary<string, string[]> LineItemElements = new()
        {
            ["InvoiceRet"]       = new[] { "InvoiceLineRet", "InvoiceLineGroupRet" },
            ["BillRet"]          = new[] { "ExpenseLineRet", "ItemLineRet", "ItemGroupLineRet" },
            ["SalesReceiptRet"]  = new[] { "SalesReceiptLineRet", "SalesReceiptLineGroupRet" },
            ["PurchaseOrderRet"] = new[] { "PurchaseOrderLineRet", "PurchaseOrderLineGroupRet" },
            ["CreditMemoRet"]    = new[] { "CreditMemoLineRet", "CreditMemoLineGroupRet" },
            ["EstimateRet"]      = new[] { "EstimateLineRet", "EstimateLineGroupRet" },
            ["CheckRet"]         = new[] { "ExpenseLineRet", "ItemLineRet", "ItemGroupLineRet" },
            ["VendorCreditRet"]  = new[] { "ExpenseLineRet", "ItemLineRet", "ItemGroupLineRet" },
            ["DepositRet"]       = new[] { "DepositLineRet" },
            // FIX #12: JournalEntryRet uses BOTH naming conventions depending on QB version:
            //   - "JournalDebitLineRet" / "JournalCreditLineRet" (standard *Ret suffix)
            //   - "JournalDebitLine" / "JournalCreditLine" (no suffix — used by QB 2023)
            // We must check for BOTH to ensure line items are captured regardless of QB version.
            ["JournalEntryRet"]  = new[] { "JournalDebitLineRet", "JournalCreditLineRet",
                                           "JournalDebitLine", "JournalCreditLine" },
        };

        public DataExporter(QBConnectionManager connection, ExportConfig exportConfig,
            string outputDirectory, string sdkVersion)
        {
            _connection = connection;
            _exportConfig = exportConfig;
            _outputDirectory = outputDirectory;
            _sdkVersion = sdkVersion;
            _effectiveSDKVersion = sdkVersion;
        }

        /// <summary>
        /// Detects the QB SDK version by sending a HostQuery request.
        /// Adjusts the effective SDK version to match what QB supports.
        /// Call this after connecting but before exporting.
        /// </summary>
        public string DetectAndAdjustSDKVersion()
        {
            try
            {
                Log.Information("Detecting QB SDK version...");
                var hostQueryXml = QBSDKVersionHelper.BuildHostQueryRequest();
                var response = _connection.ProcessRequest(hostQueryXml);

                var (maxVersion, supportedVersions) = QBSDKVersionHelper.ParseHostQueryResponse(response);
                _detectedMaxSDKVersion = maxVersion;

                // Use the minimum of configured and detected max version
                _effectiveSDKVersion = QBSDKVersionHelper.DetermineEffectiveSDKVersion(_sdkVersion, maxVersion);

                Log.Information("SDK version detection: configured={Configured}, detected_max={Detected}, using={Effective}",
                    _sdkVersion, maxVersion, _effectiveSDKVersion);

                return _effectiveSDKVersion;
            }
            catch (Exception ex)
            {
                Log.Warning("SDK version detection failed: {Message}. Using configured version: {Version}",
                    ex.Message, _sdkVersion);
                _effectiveSDKVersion = _sdkVersion;
                return _effectiveSDKVersion;
            }
        }

        /// <summary>
        /// Gets the effective SDK version being used (after detection/adjustment).
        /// </summary>
        public string EffectiveSDKVersion => _effectiveSDKVersion;

        /// <summary>
        /// Entity types that support the ActiveStatus/IsActive filter in QBXML queries.
        /// These are "list" entities (not transactions) that can be marked inactive.
        /// </summary>
        private static readonly HashSet<string> ActiveStatusEntityTypes = new()
        {
            "Accounts", "Customers", "Vendors", "Employees", "Items",
            "PaymentMethods", "Terms", "Classes", "SalesTaxCodes", "ShipMethods",
            "CustomerTypes", "VendorTypes", "JobTypes", "PriceLevels",
            // FIX #9: ItemSalesTax supports ActiveStatus filter; include inactive
            // sales-tax items in export so reactivated entities can resolve their
            // ItemSalesTaxRef back to a known sales-tax item in QB 2021.
            "ItemSalesTax",
            // FIX #10: CustomerMsgs supports ActiveStatus filter — include inactive
            // messages too so referenced (but possibly retired) CustomerMsgs still
            // round-trip into the destination QB.
            "CustomerMsgs"
        };

        /// <summary>
        /// Exports all configured entity types from QuickBooks 2023.
        /// Includes both active and inactive records for list entities.
        /// </summary>
        public Dictionary<string, ExportedEntitySet> ExportAll()
        {
            var allExports = new Dictionary<string, ExportedEntitySet>();

            Log.Information("═══════════════════════════════════════════════════════");
            Log.Information("  STARTING DATA EXPORT FROM QUICKBOOKS 2023");
            Log.Information("  Include inactive records: {IncludeInactive}", _exportConfig.IncludeInactiveRecords);
            Log.Information("═══════════════════════════════════════════════════════");

            Directory.CreateDirectory(_outputDirectory);

            foreach (var entityType in _exportConfig.EntityTypes)
            {
                try
                {
                    Log.Information("────────────────────────────────────────────");
                    Log.Information("Exporting: {EntityType}...", entityType);

                    var exportedSet = ExportEntityType(entityType);
                    allExports[entityType] = exportedSet;

                    // Save individual entity file
                    SaveExportedData(entityType, exportedSet);

                    Log.Information("  ✓ Exported {Count} {EntityType} records (Active: {Active}, Inactive: {Inactive})",
                        exportedSet.TotalCount, entityType, exportedSet.ActiveCount, exportedSet.InactiveCount);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "  ✗ Failed to export {EntityType}: {Message}", entityType, ex.Message);

                    allExports[entityType] = new ExportedEntitySet
                    {
                        EntityType = entityType,
                        TotalCount = 0,
                        Entities = new List<QBEntity>()
                    };
                }
            }

            // Save master export manifest
            SaveExportManifest(allExports);

            // Log inactive entity summary
            LogInactiveEntitySummary(allExports);

            Log.Information("═══════════════════════════════════════════════════════");
            Log.Information("  EXPORT COMPLETE: {Total} total records across {Types} entity types",
                allExports.Values.Sum(e => e.TotalCount), allExports.Count);
            Log.Information("═══════════════════════════════════════════════════════");

            return allExports;
        }

        /// <summary>
        /// Logs a summary of inactive entity counts across all exported entity types.
        /// </summary>
        private void LogInactiveEntitySummary(Dictionary<string, ExportedEntitySet> allExports)
        {
            var inactiveEntities = allExports
                .Where(kvp => kvp.Value.InactiveCount > 0)
                .OrderByDescending(kvp => kvp.Value.InactiveCount)
                .ToList();

            if (inactiveEntities.Any())
            {
                Log.Information("────────────────────────────────────────────");
                Log.Information("  INACTIVE ENTITY SUMMARY");
                Log.Information("────────────────────────────────────────────");

                foreach (var (entityType, data) in inactiveEntities)
                {
                    Log.Information("    {EntityType}: {InactiveCount} inactive out of {TotalCount} total",
                        entityType, data.InactiveCount, data.TotalCount);
                }

                var totalInactive = inactiveEntities.Sum(kvp => kvp.Value.InactiveCount);
                Log.Information("    ─────────────────────────────────────");
                Log.Information("    Total inactive records: {Total}", totalInactive);
            }
            else
            {
                Log.Information("  No inactive records found across all entity types.");
            }
        }

        /// <summary>
        /// Exports all records for a single entity type.
        /// For list entities (Customers, Vendors, Items, etc.), includes both active and inactive
        /// records when IncludeInactiveRecords is true by using ActiveStatus=All in the QBXML query.
        /// </summary>
        public ExportedEntitySet ExportEntityType(string entityType)
        {
            if (!QueryMap.ContainsKey(entityType))
            {
                Log.Warning("No QBXML query mapping for entity type: {EntityType}. Skipping.", entityType);
                return new ExportedEntitySet { EntityType = entityType };
            }

            var (queryType, responseType) = QueryMap[entityType];
            var isTransaction = TransactionTypes.Contains(entityType);
            var supportsActiveStatus = ActiveStatusEntityTypes.Contains(entityType);

            // Build the query - use effective SDK version (auto-adjusted for QB compatibility)
            var includeInactive = supportsActiveStatus && _exportConfig.IncludeInactiveRecords;

            // FIX #11: Include line items for transaction types that have them.
            // Without <IncludeLineItems>true</IncludeLineItems>, QBXML queries
            // return ONLY header fields — no ExpenseLineRet, DepositLineRet,
            // JournalDebitLineRet/JournalCreditLineRet, SalesReceiptLineRet, etc.
            // This caused 100% failure (0/1,467) on all 4 line-item transaction types.
            var hasLineItems = LineItemElements.ContainsKey(responseType);

            var qbxmlRequest = QBConnectionManager.BuildQueryRequest(
                queryType,
                _effectiveSDKVersion,  // Use effective version (may be adjusted from configured)
                includeInactive: includeInactive,
                fromDate: isTransaction ? _exportConfig.DateRangeStart : null,
                toDate: isTransaction ? _exportConfig.DateRangeEnd : null,
                includeLineItems: hasLineItems
            );

            if (includeInactive)
            {
                Log.Debug("  Including inactive records for {EntityType} (ActiveStatus=All)", entityType);
            }
            if (hasLineItems)
            {
                Log.Information("  FIX #11: Including line items for {EntityType} (IncludeLineItems=true)", entityType);
            }

            // Execute query
            var response = _connection.ProcessRequestWithRetry(qbxmlRequest);

            // Parse response
            var xmlEntities = QBConnectionManager.ParseResponseEntities(response, responseType);

            // Convert XML entities to our model
            var entities = new List<QBEntity>();
            foreach (var xmlEntity in xmlEntities)
            {
                try
                {
                    var entity = ConvertXmlToEntity(xmlEntity, entityType, responseType);
                    entities.Add(entity);
                }
                catch (Exception ex)
                {
                    Log.Warning("Error converting {EntityType} record: {Message}", entityType, ex.Message);
                }
            }

            // Calculate active/inactive counts
            var activeCount = entities.Count(e => e.IsActive);
            var inactiveCount = entities.Count(e => !e.IsActive);

            // FIX #11: Log line item counts for diagnostic verification
            if (hasLineItems)
            {
                var entitiesWithLines = entities.Count(e => e.LineItems.Count > 0);
                var totalLineItems = entities.Sum(e => e.LineItems.Count);
                Log.Information("  FIX #11: {EntityType} line item stats: {WithLines}/{Total} entities have line items, {TotalLines} total line items exported",
                    entityType, entitiesWithLines, entities.Count, totalLineItems);
                if (entitiesWithLines == 0 && entities.Count > 0)
                {
                    Log.Warning("  FIX #11 WARNING: {EntityType} has {Count} entities but ZERO line items! " +
                        "This may indicate IncludeLineItems is not being applied correctly.",
                        entityType, entities.Count);
                }
            }

            return new ExportedEntitySet
            {
                EntityType = entityType,
                SourceVersion = "QB2023",
                ExportTimestamp = DateTime.UtcNow,
                TotalCount = entities.Count,
                ActiveCount = activeCount,
                InactiveCount = inactiveCount,
                Entities = entities
            };
        }

        /// <summary>
        /// Converts a QBXML response element to a QBEntity model with all fields as dynamic JSON.
        /// </summary>
        private QBEntity ConvertXmlToEntity(XElement xmlElement, string entityType, string responseType)
        {
            var entity = new QBEntity
            {
                EntityType = entityType,
                ExportedAt = DateTime.UtcNow
            };

            // Extract standard identifiers
            entity.ListID = xmlElement.Element("ListID")?.Value ?? string.Empty;
            entity.TxnID = xmlElement.Element("TxnID")?.Value ?? string.Empty;
            entity.Name = xmlElement.Element("Name")?.Value ?? string.Empty;
            entity.FullName = xmlElement.Element("FullName")?.Value ?? entity.Name;

            // Extract IsActive status (defaults to true if not present, e.g. for transactions)
            var isActiveValue = xmlElement.Element("IsActive")?.Value;
            entity.IsActive = string.IsNullOrEmpty(isActiveValue) || 
                string.Equals(isActiveValue, "true", StringComparison.OrdinalIgnoreCase);

            // Convert all fields to a flat JObject
            entity.Fields = ConvertXmlToJObject(xmlElement, responseType);

            // Extract line items for transaction types
            if (LineItemElements.ContainsKey(responseType))
            {
                foreach (var lineElementName in LineItemElements[responseType])
                {
                    foreach (var lineElement in xmlElement.Elements(lineElementName))
                    {
                        var lineItem = ConvertXmlToJObject(lineElement, lineElementName);
                        lineItem["_lineType"] = lineElementName;
                        entity.LineItems.Add(lineItem);
                    }
                }
            }

            return entity;
        }

        /// <summary>
        /// Recursively converts an XML element and its children to a JObject.
        /// Handles nested elements (like Address blocks) by flattening with dot notation.
        /// </summary>
        private JObject ConvertXmlToJObject(XElement element, string skipLineItems = "")
        {
            var obj = new JObject();

            foreach (var child in element.Elements())
            {
                var name = child.Name.LocalName;

                // Skip line item elements (they're handled separately)
                if (!string.IsNullOrEmpty(skipLineItems) && LineItemElements.ContainsKey(skipLineItems))
                {
                    if (LineItemElements[skipLineItems].Contains(name))
                        continue;
                }

                if (child.HasElements)
                {
                    // Nested element - create sub-object
                    var subObj = ConvertXmlToJObject(child);
                    obj[name] = subObj;
                }
                else
                {
                    // Leaf element - store value
                    obj[name] = child.Value;
                }
            }

            return obj;
        }

        /// <summary>
        /// Saves exported data for one entity type to a JSON file.
        /// </summary>
        private void SaveExportedData(string entityType, ExportedEntitySet data)
        {
            var filePath = Path.Combine(_outputDirectory, $"{entityType}.json");
            var json = JsonConvert.SerializeObject(data, Formatting.Indented, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });
            File.WriteAllText(filePath, json);

            Log.Debug("Saved {EntityType} export to: {FilePath}", entityType, filePath);
        }

        /// <summary>
        /// Saves a manifest file listing all exports with counts and timestamps.
        /// </summary>
        private void SaveExportManifest(Dictionary<string, ExportedEntitySet> allExports)
        {
            var manifest = new
            {
                ExportTimestamp = DateTime.UtcNow,
                SourceVersion = "QuickBooks 2023",
                SDKVersion = _sdkVersion,
                EffectiveSDKVersion = _effectiveSDKVersion,
                DetectedMaxSDKVersion = _detectedMaxSDKVersion ?? "not detected",
                IncludeInactiveRecords = _exportConfig.IncludeInactiveRecords,
                TotalRecords = allExports.Values.Sum(e => e.TotalCount),
                TotalActiveRecords = allExports.Values.Sum(e => e.ActiveCount),
                TotalInactiveRecords = allExports.Values.Sum(e => e.InactiveCount),
                EntitySummary = allExports.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new
                    {
                        Count = kvp.Value.TotalCount,
                        Active = kvp.Value.ActiveCount,
                        Inactive = kvp.Value.InactiveCount,
                        kvp.Value.ExportTimestamp
                    }
                )
            };

            var filePath = Path.Combine(_outputDirectory, "_ExportManifest.json");
            var json = JsonConvert.SerializeObject(manifest, Formatting.Indented);
            File.WriteAllText(filePath, json);

            Log.Information("Export manifest saved to: {FilePath}", filePath);
        }

        /// <summary>
        /// Loads previously exported data from files (for re-processing without re-querying QB).
        /// Includes FIX #15: Post-deserialization recovery for LineItems that may not
        /// have been mapped during standard deserialization.
        /// </summary>
        public static Dictionary<string, ExportedEntitySet> LoadExportedData(string exportDirectory)
        {
            var allExports = new Dictionary<string, ExportedEntitySet>();

            foreach (var file in Directory.GetFiles(exportDirectory, "*.json"))
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                if (fileName.StartsWith("_")) continue; // Skip manifest files

                try
                {
                    var json = File.ReadAllText(file);
                    var data = JsonConvert.DeserializeObject<ExportedEntitySet>(json);
                    if (data != null)
                    {
                        // ═══════════════════════════════════════════════════════════
                        // FIX #15: Post-deserialization LineItems recovery
                        // ═══════════════════════════════════════════════════════════
                        // In some scenarios, the "LineItems" array in the JSON file
                        // is not automatically mapped to QBEntity.LineItems during
                        // deserialization (e.g., naming policy mismatch, serializer
                        // config differences between save and load). This caused
                        // 30/30 SalesReceipts to fail because the importer generated
                        // QBXML without any <SalesReceiptLineAdd> elements.
                        //
                        // Recovery strategy: parse the raw JSON as JObject, then for
                        // each entity with empty LineItems, check if the JSON has a
                        // "LineItems" array and manually hydrate it.
                        // ═══════════════════════════════════════════════════════════
                        RecoverLineItemsIfNeeded(json, data);

                        allExports[data.EntityType] = data;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning("Could not load export file {File}: {Message}", file, ex.Message);
                }
            }

            return allExports;
        }

        /// <summary>
        /// FIX #15: Checks if any entities in the loaded data set have empty LineItems
        /// despite the raw JSON containing a non-empty "LineItems" array. If so,
        /// manually deserializes the line items from the raw JSON to recover them.
        /// This is a defensive fallback — if deserialization works correctly,
        /// this method is a no-op.
        /// </summary>
        private static void RecoverLineItemsIfNeeded(string rawJson, ExportedEntitySet data)
        {
            // Quick check: are there ANY entities with empty LineItems?
            bool anyEmpty = data.Entities.Any(e => e.LineItems == null || e.LineItems.Count == 0);
            if (!anyEmpty) return;

            // Does the raw JSON even mention "LineItems" with content?
            if (!rawJson.Contains("\"LineItems\""))
                return;

            try
            {
                // Parse the raw JSON to access the Entities array directly
                var rawObj = JObject.Parse(rawJson);
                var rawEntities = rawObj["Entities"] as JArray;
                if (rawEntities == null) return;

                int recovered = 0;
                for (int i = 0; i < data.Entities.Count && i < rawEntities.Count; i++)
                {
                    var entity = data.Entities[i];
                    if (entity.LineItems != null && entity.LineItems.Count > 0)
                        continue; // Already has line items — skip

                    var rawEntity = rawEntities[i] as JObject;
                    if (rawEntity == null) continue;

                    // Check for "LineItems" in the raw entity JSON (case-insensitive)
                    var lineItemsToken = rawEntity["LineItems"]
                                      ?? rawEntity["lineItems"]
                                      ?? rawEntity["lineitems"];

                    if (lineItemsToken is JArray lineItemsArray && lineItemsArray.Count > 0)
                    {
                        entity.LineItems = lineItemsArray
                            .OfType<JObject>()
                            .Select(li => li.DeepClone() as JObject ?? new JObject())
                            .ToList();

                        recovered++;
                        Log.Debug("  FIX #15: Recovered {Count} line items for '{Name}' (index {Index})",
                            entity.LineItems.Count,
                            entity.Name ?? entity.TxnID ?? $"index-{i}",
                            i);
                    }
                }

                if (recovered > 0)
                {
                    Log.Warning("  FIX #15: Recovered LineItems for {Count}/{Total} {EntityType} entities " +
                        "that lost them during deserialization",
                        recovered, data.Entities.Count, data.EntityType);
                }
            }
            catch (Exception ex)
            {
                Log.Warning("  FIX #15: LineItems recovery failed for {EntityType}: {Message}",
                    data.EntityType, ex.Message);
            }
        }
    }
}
