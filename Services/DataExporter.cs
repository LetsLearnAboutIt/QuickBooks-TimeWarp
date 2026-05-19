using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using QB_TimeWarp.Models;
using Serilog;

namespace QB_TimeWarp.Services
{
    /// <summary>
    /// Exports all entity data from QuickBooks 2023 via QBXML SDK.
    /// Data is serialized to JSON files organized by entity type.
    /// </summary>
    public class DataExporter
    {
        private readonly QBConnectionManager _connection;
        private readonly ExportConfig _exportConfig;
        private readonly string _outputDirectory;
        private readonly string _sdkVersion;

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
            ["PaymentMethods"]  = ("PaymentMethodQuery",   "PaymentMethodRet"),
            ["Terms"]           = ("TermsQuery",           "StandardTermsRet"),
            ["Classes"]         = ("ClassQuery",           "ClassRet"),
            ["SalesTaxCodes"]   = ("SalesTaxCodeQuery",    "SalesTaxCodeRet"),
            ["ShipMethods"]     = ("ShipMethodQuery",      "ShipMethodRet"),
            ["CustomerTypes"]   = ("CustomerTypeQuery",    "CustomerTypeRet"),
            ["VendorTypes"]     = ("VendorTypeQuery",      "VendorTypeRet"),
            ["JobTypes"]        = ("JobTypeQuery",         "JobTypeRet"),
            ["PriceLevels"]     = ("PriceLevelQuery",      "PriceLevelRet"),

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
            ["JournalEntryRet"]  = new[] { "JournalDebitLineRet", "JournalCreditLineRet" },
        };

        public DataExporter(QBConnectionManager connection, ExportConfig exportConfig,
            string outputDirectory, string sdkVersion)
        {
            _connection = connection;
            _exportConfig = exportConfig;
            _outputDirectory = outputDirectory;
            _sdkVersion = sdkVersion;
        }

        /// <summary>
        /// Exports all configured entity types from QuickBooks 2023.
        /// </summary>
        public Dictionary<string, ExportedEntitySet> ExportAll()
        {
            var allExports = new Dictionary<string, ExportedEntitySet>();

            Log.Information("═══════════════════════════════════════════════════════");
            Log.Information("  STARTING DATA EXPORT FROM QUICKBOOKS 2023");
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

                    Log.Information("  ✓ Exported {Count} {EntityType} records",
                        exportedSet.TotalCount, entityType);
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

            Log.Information("═══════════════════════════════════════════════════════");
            Log.Information("  EXPORT COMPLETE: {Total} total records across {Types} entity types",
                allExports.Values.Sum(e => e.TotalCount), allExports.Count);
            Log.Information("═══════════════════════════════════════════════════════");

            return allExports;
        }

        /// <summary>
        /// Exports all records for a single entity type.
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

            // Build the query
            var qbxmlRequest = QBConnectionManager.BuildQueryRequest(
                queryType,
                _sdkVersion,
                includeInactive: _exportConfig.IncludeInactiveRecords,
                fromDate: isTransaction ? _exportConfig.DateRangeStart : null,
                toDate: isTransaction ? _exportConfig.DateRangeEnd : null
            );

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

            return new ExportedEntitySet
            {
                EntityType = entityType,
                SourceVersion = "QB2023",
                ExportTimestamp = DateTime.UtcNow,
                TotalCount = entities.Count,
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
                TotalRecords = allExports.Values.Sum(e => e.TotalCount),
                EntitySummary = allExports.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new { Count = kvp.Value.TotalCount, kvp.Value.ExportTimestamp }
                )
            };

            var filePath = Path.Combine(_outputDirectory, "_ExportManifest.json");
            var json = JsonConvert.SerializeObject(manifest, Formatting.Indented);
            File.WriteAllText(filePath, json);

            Log.Information("Export manifest saved to: {FilePath}", filePath);
        }

        /// <summary>
        /// Loads previously exported data from files (for re-processing without re-querying QB).
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
    }
}
