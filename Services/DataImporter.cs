using System.Text;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using QB_TimeWarp.Models;
using Serilog;

namespace QB_TimeWarp.Services
{
    /// <summary>
    /// Imports transformed data into QuickBooks 2021 via QBXML SDK.
    /// Handles dependency ordering, ID remapping, and error recovery.
    /// </summary>
    public class DataImporter
    {
        private readonly QBConnectionManager _connection;
        private readonly ImportConfig _importConfig;
        private readonly TransformationRulesConfig _transformationRules;
        private readonly string _sdkVersion;

        /// <summary>
        /// Stores mappings from source IDs/Names to newly assigned target IDs.
        /// Used to resolve references when importing transactions that depend on list items.
        /// </summary>
        private readonly Dictionary<string, IdMapping> _idMappings = new();
        private readonly Dictionary<string, string> _nameToListIdMap = new();

        /// <summary>
        /// Classes that exist in QB 2021 (populated during import).
        /// </summary>
        private readonly HashSet<string> _existingClasses = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _createdClasses = new();

        /// <summary>
        /// Maps our entity type names to QBXML Add request type names.
        /// </summary>
        private static readonly Dictionary<string, (string AddRequestType, string AddElementType, string ResponseType)> AddMap = new()
        {
            // Lists
            ["Accounts"]        = ("AccountAddRq",       "AccountAdd",       "AccountRet"),
            ["Customers"]       = ("CustomerAddRq",      "CustomerAdd",      "CustomerRet"),
            ["Vendors"]         = ("VendorAddRq",        "VendorAdd",        "VendorRet"),
            ["Employees"]       = ("EmployeeAddRq",      "EmployeeAdd",      "EmployeeRet"),
            ["PaymentMethods"]  = ("PaymentMethodAddRq", "PaymentMethodAdd", "PaymentMethodRet"),
            ["Terms"]           = ("StandardTermsAddRq", "StandardTermsAdd", "StandardTermsRet"),
            ["Classes"]         = ("ClassAddRq",         "ClassAdd",         "ClassRet"),
            ["SalesTaxCodes"]   = ("SalesTaxCodeAddRq",  "SalesTaxCodeAdd",  "SalesTaxCodeRet"),
            ["ShipMethods"]     = ("ShipMethodAddRq",    "ShipMethodAdd",    "ShipMethodRet"),
            ["CustomerTypes"]   = ("CustomerTypeAddRq",  "CustomerTypeAdd",  "CustomerTypeRet"),
            ["VendorTypes"]     = ("VendorTypeAddRq",    "VendorTypeAdd",    "VendorTypeRet"),
            ["JobTypes"]        = ("JobTypeAddRq",       "JobTypeAdd",       "JobTypeRet"),
            ["PriceLevels"]     = ("PriceLevelAddRq",    "PriceLevelAdd",    "PriceLevelRet"),

            // Transactions
            ["Invoices"]        = ("InvoiceAddRq",       "InvoiceAdd",       "InvoiceRet"),
            ["Bills"]           = ("BillAddRq",          "BillAdd",          "BillRet"),
            ["Payments"]        = ("ReceivePaymentAddRq","ReceivePaymentAdd","ReceivePaymentRet"),
            ["SalesReceipts"]   = ("SalesReceiptAddRq",  "SalesReceiptAdd",  "SalesReceiptRet"),
            ["PurchaseOrders"]  = ("PurchaseOrderAddRq", "PurchaseOrderAdd", "PurchaseOrderRet"),
            ["JournalEntries"]  = ("JournalEntryAddRq",  "JournalEntryAdd",  "JournalEntryRet"),
            ["CreditMemos"]     = ("CreditMemoAddRq",    "CreditMemoAdd",    "CreditMemoRet"),
            ["Estimates"]       = ("EstimateAddRq",      "EstimateAdd",      "EstimateRet"),
            ["Deposits"]        = ("DepositAddRq",       "DepositAdd",       "DepositRet"),
            ["Checks"]          = ("CheckAddRq",         "CheckAdd",         "CheckRet"),
            ["VendorCredits"]   = ("VendorCreditAddRq",  "VendorCreditAdd",  "VendorCreditRet"),
            ["InventoryAdjustments"] = ("InventoryAdjustmentAddRq", "InventoryAdjustmentAdd", "InventoryAdjustmentRet"),
            ["Transfers"]       = ("TransferAddRq",      "TransferAdd",      "TransferRet"),
        };

        /// <summary>
        /// Item subtypes need specific Add request types.
        /// </summary>
        private static readonly Dictionary<string, (string AddReq, string AddElem, string RetType)> ItemSubTypeMap = new()
        {
            ["ItemService"]      = ("ItemServiceAddRq",      "ItemServiceAdd",      "ItemServiceRet"),
            ["ItemInventory"]    = ("ItemInventoryAddRq",    "ItemInventoryAdd",    "ItemInventoryRet"),
            ["ItemNonInventory"] = ("ItemNonInventoryAddRq", "ItemNonInventoryAdd", "ItemNonInventoryRet"),
            ["ItemOtherCharge"]  = ("ItemOtherChargeAddRq",  "ItemOtherChargeAdd",  "ItemOtherChargeRet"),
            ["ItemDiscount"]     = ("ItemDiscountAddRq",     "ItemDiscountAdd",     "ItemDiscountRet"),
            ["ItemGroup"]        = ("ItemGroupAddRq",        "ItemGroupAdd",        "ItemGroupRet"),
            ["ItemSalesTax"]     = ("ItemSalesTaxAddRq",     "ItemSalesTaxAdd",     "ItemSalesTaxRet"),
        };

        /// <summary>
        /// Fields that are references to other entities (need FullName resolution).
        /// </summary>
        private static readonly HashSet<string> ReferenceFields = new(StringComparer.OrdinalIgnoreCase)
        {
            "CustomerRef", "VendorRef", "AccountRef", "ARAccountRef", "APAccountRef",
            "IncomeAccountRef", "COGSAccountRef", "AssetAccountRef", "ExpenseAccountRef",
            "TermsRef", "SalesRepRef", "SalesTaxCodeRef", "ItemSalesTaxRef",
            "PreferredPaymentMethodRef", "PaymentMethodRef", "ClassRef",
            "ShipMethodRef", "DepositToAccountRef", "TemplateRef",
            "ItemRef", "PriceLevelRef", "CustomerTypeRef", "VendorTypeRef",
            "ParentRef", "EntityRef", "CurrencyRef"
        };

        /// <summary>
        /// Fields that should be excluded from Add requests (read-only or system-generated).
        /// </summary>
        private static readonly HashSet<string> ExcludedFields = new(StringComparer.OrdinalIgnoreCase)
        {
            "ListID", "TxnID", "TimeCreated", "TimeModified", "EditSequence",
            "TxnNumber", "Balance", "TotalBalance", "Subtotal", "BalanceRemaining",
            "IsPaid", "ExternalGUID", "FullName", "OpenBalance"
        };

        public DataImporter(QBConnectionManager connection, ImportConfig importConfig, string sdkVersion)
            : this(connection, importConfig, sdkVersion, new TransformationRulesConfig())
        {
        }

        public DataImporter(QBConnectionManager connection, ImportConfig importConfig, string sdkVersion,
            TransformationRulesConfig transformationRules)
        {
            _connection = connection;
            _importConfig = importConfig;
            _sdkVersion = sdkVersion;
            _transformationRules = transformationRules;
        }

        /// <summary>
        /// Imports all data into QuickBooks 2021 following the configured import order.
        /// </summary>
        public MigrationReport ImportAll(Dictionary<string, ExportedEntitySet> transformedData)
        {
            var report = new MigrationReport
            {
                StartTime = DateTime.UtcNow,
                SourceCompanyFile = "QB 2023",
                TargetCompanyFile = "QB 2021"
            };

            Log.Information("═══════════════════════════════════════════════════════");
            Log.Information("  STARTING DATA IMPORT INTO QUICKBOOKS 2021");
            Log.Information("═══════════════════════════════════════════════════════");

            if (_importConfig.DryRun)
            {
                Log.Warning("*** DRY RUN MODE - No data will actually be imported ***");
            }

            foreach (var entityType in _importConfig.ImportOrder)
            {
                if (!transformedData.ContainsKey(entityType))
                {
                    Log.Debug("No data for {EntityType}, skipping.", entityType);
                    continue;
                }

                var entitySet = transformedData[entityType];
                if (entitySet.TotalCount == 0)
                {
                    Log.Debug("{EntityType} has 0 records, skipping.", entityType);
                    continue;
                }

                Log.Information("────────────────────────────────────────────");
                Log.Information("Importing: {EntityType} ({Count} records)...", entityType, entitySet.TotalCount);

                var batchSummary = ImportEntitySet(entityType, entitySet);
                report.EntitySummaries[entityType] = batchSummary;

                Log.Information("  Result: {Succeeded} succeeded, {Failed} failed, {Skipped} skipped ({Duration:F1}s)",
                    batchSummary.Succeeded, batchSummary.Failed, batchSummary.Skipped,
                    batchSummary.Duration.TotalSeconds);
            }

            report.EndTime = DateTime.UtcNow;
            report.OverallStatus = report.TotalRecordsFailed == 0
                ? MigrationStatus.Completed
                : report.TotalRecordsSucceeded > 0
                    ? MigrationStatus.CompletedWithErrors
                    : MigrationStatus.Failed;

            Log.Information("═══════════════════════════════════════════════════════");
            Log.Information("  IMPORT COMPLETE: {Succeeded}/{Total} records imported successfully",
                report.TotalRecordsSucceeded, report.TotalRecordsAttempted);
            if (report.TotalRecordsFailed > 0)
                Log.Warning("  {Failed} records failed to import", report.TotalRecordsFailed);
            Log.Information("═══════════════════════════════════════════════════════");

            return report;
        }

        /// <summary>
        /// Imports all entities of a single type.
        /// </summary>
        private ImportBatchSummary ImportEntitySet(string entityType, ExportedEntitySet entitySet)
        {
            var summary = new ImportBatchSummary
            {
                EntityType = entityType,
                TotalAttempted = entitySet.TotalCount
            };

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Skip non-importable types
            if (entityType == "Preferences" || entityType == "CompanyInfo")
            {
                Log.Information("  Skipping {EntityType} (settings cannot be imported via SDK).", entityType);
                summary.Skipped = entitySet.TotalCount;
                stopwatch.Stop();
                summary.Duration = stopwatch.Elapsed;
                return summary;
            }

            // Process in batches
            var batch = new List<QBEntity>();
            int processed = 0;

            foreach (var entity in entitySet.Entities)
            {
                processed++;
                try
                {
                    if (_importConfig.DryRun)
                    {
                        // Validate the request would be valid without sending
                        var xml = BuildAddRequestXml(entityType, entity);
                        if (!string.IsNullOrEmpty(xml))
                        {
                            summary.Succeeded++;
                            Log.Debug("  [DRY RUN] Would import: {Name}", entity.Name);
                        }
                        continue;
                    }

                    var result = ImportSingleEntity(entityType, entity);

                    if (result.Success)
                    {
                        summary.Succeeded++;

                        // Store ID mapping for reference resolution
                        StoreIdMapping(entityType, entity, result);

                        if (processed % 50 == 0)
                        {
                            Log.Information("  Progress: {Processed}/{Total}...",
                                processed, entitySet.TotalCount);
                        }
                    }
                    else
                    {
                        summary.Failed++;
                        summary.FailedRecords.Add(result);

                        if (_importConfig.SkipOnError)
                        {
                            Log.Warning("  ✗ Skipped '{Name}': {Error}",
                                entity.Name, result.ErrorMessage);
                        }
                        else
                        {
                            throw new QBRequestException(
                                $"Import failed for {entityType} '{entity.Name}': {result.ErrorMessage}");
                        }
                    }
                }
                catch (Exception ex) when (_importConfig.SkipOnError)
                {
                    summary.Failed++;
                    summary.FailedRecords.Add(new ImportResult
                    {
                        EntityType = entityType,
                        SourceIdentifier = entity.Name,
                        Success = false,
                        ErrorMessage = ex.Message
                    });
                    Log.Warning("  ✗ Error importing '{Name}': {Message}", entity.Name, ex.Message);
                }
            }

            stopwatch.Stop();
            summary.Duration = stopwatch.Elapsed;
            return summary;
        }

        /// <summary>
        /// Imports a single entity into QuickBooks 2021.
        /// </summary>
        private ImportResult ImportSingleEntity(string entityType, QBEntity entity)
        {
            var result = new ImportResult
            {
                EntityType = entityType,
                SourceIdentifier = entity.FullName ?? entity.Name
            };

            try
            {
                var requestXml = BuildAddRequestXml(entityType, entity);
                if (string.IsNullOrEmpty(requestXml))
                {
                    result.Success = false;
                    result.ErrorMessage = "Could not build QBXML request.";
                    return result;
                }

                result.QBXMLRequest = requestXml;

                // Send to QuickBooks
                var response = _connection.ProcessRequestWithRetry(requestXml);
                result.QBXMLResponse = response;

                // Parse response for success/failure
                var doc = XDocument.Parse(response);
                var rsElements = doc.Descendants()
                    .Where(e => e.Name.LocalName.EndsWith("Rs"))
                    .FirstOrDefault();

                if (rsElements != null)
                {
                    var statusCode = rsElements.Attribute("statusCode")?.Value;
                    var statusMessage = rsElements.Attribute("statusMessage")?.Value;

                    if (statusCode == "0")
                    {
                        result.Success = true;

                        // Extract new ListID or TxnID from response
                        var retElement = rsElements.Elements().FirstOrDefault();
                        if (retElement != null)
                        {
                            result.NewListID = retElement.Element("ListID")?.Value;
                            result.NewTxnID = retElement.Element("TxnID")?.Value;
                        }
                    }
                    else
                    {
                        result.Success = false;
                        result.ErrorCode = statusCode;
                        result.ErrorMessage = statusMessage;
                    }
                }
                else
                {
                    result.Success = false;
                    result.ErrorMessage = "No response status found in QBXML response.";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Builds the QBXML Add request XML for a single entity.
        /// </summary>
        private string BuildAddRequestXml(string entityType, QBEntity entity)
        {
            // Handle Items specially - they need different Add types based on item subtype
            if (entityType == "Items")
            {
                return BuildItemAddRequest(entity);
            }

            if (!AddMap.ContainsKey(entityType))
            {
                Log.Warning("No Add request mapping for entity type: {EntityType}", entityType);
                return string.Empty;
            }

            var (addReqType, addElemType, _) = AddMap[entityType];

            var fieldsXml = BuildFieldsXml(entity.Fields);
            var lineItemsXml = BuildLineItemsXml(entityType, entity.LineItems);

            var innerXml = new StringBuilder();
            innerXml.AppendLine($"<{addReqType}>");
            innerXml.AppendLine($"  <{addElemType}>");
            innerXml.Append(fieldsXml);
            if (!string.IsNullOrEmpty(lineItemsXml))
            {
                innerXml.Append(lineItemsXml);
            }
            innerXml.AppendLine($"  </{addElemType}>");
            innerXml.AppendLine($"</{addReqType}>");

            return QBConnectionManager.BuildQBXMLRequest(innerXml.ToString(), _sdkVersion);
        }

        /// <summary>
        /// Builds the QBXML Add request for an Item, using the correct subtype.
        /// </summary>
        private string BuildItemAddRequest(QBEntity entity)
        {
            var itemType = entity.Fields["Type"]?.ToString() ?? "ItemService";

            // Normalize the type name
            if (!itemType.StartsWith("Item"))
                itemType = $"Item{itemType}";

            if (!ItemSubTypeMap.ContainsKey(itemType))
            {
                Log.Warning("Unknown item type '{ItemType}' for item '{Name}'. Defaulting to ItemService.",
                    itemType, entity.Name);
                itemType = "ItemService";
            }

            var (addReq, addElem, _) = ItemSubTypeMap[itemType];

            // Remove the Type field since it's implicit in the request type
            var fieldsClone = entity.Fields.DeepClone() as JObject ?? new JObject();
            fieldsClone.Remove("Type");

            var fieldsXml = BuildFieldsXml(fieldsClone);

            var innerXml = new StringBuilder();
            innerXml.AppendLine($"<{addReq}>");
            innerXml.AppendLine($"  <{addElem}>");
            innerXml.Append(fieldsXml);
            innerXml.AppendLine($"  </{addElem}>");
            innerXml.AppendLine($"</{addReq}>");

            return QBConnectionManager.BuildQBXMLRequest(innerXml.ToString(), _sdkVersion);
        }

        /// <summary>
        /// Converts a JObject of fields to QBXML element format.
        /// Handles nested objects (Address, Ref types, etc.).
        /// </summary>
        private string BuildFieldsXml(JObject fields)
        {
            var sb = new StringBuilder();

            foreach (var prop in fields.Properties())
            {
                if (ExcludedFields.Contains(prop.Name))
                    continue;

                if (prop.Name.StartsWith("_"))
                    continue; // Skip internal metadata fields

                if (prop.Value is JObject nested)
                {
                    // Check if it's a reference type (has FullName or ListID child)
                    if (nested["FullName"] != null || nested["ListID"] != null)
                    {
                        sb.AppendLine($"    <{prop.Name}>");
                        if (nested["FullName"] != null)
                            sb.AppendLine($"      <FullName>{EscapeXml(nested["FullName"]!.ToString())}</FullName>");
                        else if (nested["ListID"] != null)
                            sb.AppendLine($"      <ListID>{EscapeXml(nested["ListID"]!.ToString())}</ListID>");
                        sb.AppendLine($"    </{prop.Name}>");
                    }
                    else
                    {
                        // Regular nested object (like Address)
                        sb.AppendLine($"    <{prop.Name}>");
                        foreach (var childProp in nested.Properties())
                        {
                            if (childProp.Value.Type != JTokenType.Null &&
                                !string.IsNullOrEmpty(childProp.Value.ToString()))
                            {
                                sb.AppendLine($"      <{childProp.Name}>{EscapeXml(childProp.Value.ToString())}</{childProp.Name}>");
                            }
                        }
                        sb.AppendLine($"    </{prop.Name}>");
                    }
                }
                else if (prop.Value.Type != JTokenType.Null && !string.IsNullOrEmpty(prop.Value.ToString()))
                {
                    sb.AppendLine($"    <{prop.Name}>{EscapeXml(prop.Value.ToString())}</{prop.Name}>");
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Builds QBXML for line items in transaction entities.
        /// </summary>
        private string BuildLineItemsXml(string entityType, List<JObject> lineItems)
        {
            if (!lineItems.Any()) return string.Empty;

            var sb = new StringBuilder();
            var lineAddType = GetLineAddType(entityType);

            foreach (var lineItem in lineItems)
            {
                var lineType = lineItem["_lineType"]?.ToString() ?? lineAddType;
                // Convert Ret suffix to Add suffix for the request
                var addLineType = lineType.Replace("Ret", "Add").Replace("LineRet", "LineAdd");
                if (!addLineType.EndsWith("Add") && !addLineType.Contains("LineAdd"))
                    addLineType = lineAddType;

                sb.AppendLine($"    <{addLineType}>");

                foreach (var prop in lineItem.Properties())
                {
                    if (prop.Name.StartsWith("_")) continue; // Skip metadata
                    if (ExcludedFields.Contains(prop.Name)) continue;

                    if (prop.Value is JObject nested)
                    {
                        sb.AppendLine($"      <{prop.Name}>");
                        foreach (var childProp in nested.Properties())
                        {
                            if (childProp.Value.Type != JTokenType.Null &&
                                !string.IsNullOrEmpty(childProp.Value.ToString()))
                            {
                                sb.AppendLine($"        <{childProp.Name}>{EscapeXml(childProp.Value.ToString())}</{childProp.Name}>");
                            }
                        }
                        sb.AppendLine($"      </{prop.Name}>");
                    }
                    else if (prop.Value.Type != JTokenType.Null && !string.IsNullOrEmpty(prop.Value.ToString()))
                    {
                        sb.AppendLine($"      <{prop.Name}>{EscapeXml(prop.Value.ToString())}</{prop.Name}>");
                    }
                }

                sb.AppendLine($"    </{addLineType}>");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Gets the line item Add element name for a given entity type.
        /// </summary>
        private static string GetLineAddType(string entityType)
        {
            return entityType switch
            {
                "Invoices" => "InvoiceLineAdd",
                "Bills" => "ExpenseLineAdd",
                "SalesReceipts" => "SalesReceiptLineAdd",
                "PurchaseOrders" => "PurchaseOrderLineAdd",
                "CreditMemos" => "CreditMemoLineAdd",
                "Estimates" => "EstimateLineAdd",
                "Checks" => "ExpenseLineAdd",
                "VendorCredits" => "ExpenseLineAdd",
                "Deposits" => "DepositLineAdd",
                "JournalEntries" => "JournalDebitLineAdd",
                _ => "LineAdd"
            };
        }

        /// <summary>
        /// Stores the ID mapping after successful import for reference resolution.
        /// </summary>
        private void StoreIdMapping(string entityType, QBEntity source, ImportResult result)
        {
            var mapping = new IdMapping
            {
                EntityType = entityType,
                SourceListID = source.ListID,
                SourceTxnID = source.TxnID,
                SourceName = source.FullName ?? source.Name,
                TargetListID = result.NewListID ?? string.Empty,
                TargetTxnID = result.NewTxnID ?? string.Empty
            };

            var key = $"{entityType}:{source.FullName ?? source.Name}";
            _idMappings[key] = mapping;

            if (!string.IsNullOrEmpty(result.NewListID))
            {
                _nameToListIdMap[$"{entityType}:{source.FullName ?? source.Name}"] = result.NewListID;
            }
        }

        /// <summary>
        /// Gets all ID mappings (useful for validation).
        /// </summary>
        public Dictionary<string, IdMapping> GetIdMappings() => _idMappings;

        /// <summary>
        /// Gets the list of classes that were created during import.
        /// </summary>
        public List<string> GetCreatedClasses() => _createdClasses;

        /// <summary>
        /// Gets the set of existing classes in QB 2021.
        /// </summary>
        public HashSet<string> GetExistingClasses() => _existingClasses;

        // ═══════════════════════════════════════════════════════════════════
        // CLASS MANAGEMENT
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Ensures all required classes exist in QB 2021. Creates missing ones.
        /// Call this before importing transactions that reference classes.
        /// </summary>
        public ClassTrackingSummary EnsureClassesExist(HashSet<string> requiredClasses)
        {
            var summary = new ClassTrackingSummary
            {
                TotalClassesInSource = requiredClasses.Count,
                SourceClasses = requiredClasses.OrderBy(c => c).ToList()
            };

            if (!requiredClasses.Any())
            {
                Log.Information("  No classes to verify.");
                return summary;
            }

            Log.Information("────────────────────────────────────────────");
            Log.Information("  ENSURING CLASSES EXIST IN QB 2021");
            Log.Information("────────────────────────────────────────────");

            // Query existing classes from QB 2021
            QueryExistingClasses();

            foreach (var className in requiredClasses.OrderBy(c => c))
            {
                if (_existingClasses.Contains(className))
                {
                    summary.ClassesAlreadyExisting++;
                    Log.Debug("    Class '{Class}' already exists in QB 2021", className);
                }
                else
                {
                    // Create the missing class
                    bool created = CreateClassInQB2021(className);
                    if (created)
                    {
                        summary.ClassesCreatedInTarget++;
                        summary.CreatedClasses.Add(className);
                        _createdClasses.Add(className);
                        Log.Information("    ✓ Created class '{Class}' in QB 2021", className);
                    }
                    else
                    {
                        summary.MissingClasses.Add(className);
                        Log.Warning("    ✗ Failed to create class '{Class}' in QB 2021", className);
                    }
                }
            }

            summary.TotalClassesInTarget = _existingClasses.Count;

            Log.Information("  Class sync complete: {Existing} existing, {Created} created, {Missing} failed",
                summary.ClassesAlreadyExisting, summary.ClassesCreatedInTarget, summary.MissingClasses.Count);

            return summary;
        }

        /// <summary>
        /// Queries QB 2021 for existing classes and populates _existingClasses.
        /// </summary>
        private void QueryExistingClasses()
        {
            try
            {
                var requestXml = $@"<ClassQueryRq>
  <ActiveStatus>All</ActiveStatus>
</ClassQueryRq>";

                var fullRequest = QBConnectionManager.BuildQBXMLRequest(requestXml, _sdkVersion);
                var response = _connection.ProcessRequestWithRetry(fullRequest);
                var doc = XDocument.Parse(response);

                foreach (var classRet in doc.Descendants("ClassRet"))
                {
                    var fullName = classRet.Element("FullName")?.Value;
                    if (!string.IsNullOrEmpty(fullName))
                    {
                        _existingClasses.Add(fullName);
                    }
                }

                Log.Information("    Found {Count} existing classes in QB 2021", _existingClasses.Count);
            }
            catch (Exception ex)
            {
                Log.Warning("    Could not query existing classes: {Message}", ex.Message);
            }
        }

        /// <summary>
        /// Creates a single class in QB 2021.
        /// Handles hierarchical classes by creating parent classes first.
        /// </summary>
        private bool CreateClassInQB2021(string className)
        {
            try
            {
                // Handle hierarchical class names (e.g., "Department:Marketing")
                var parts = className.Split(':');
                if (parts.Length > 1)
                {
                    // Ensure parent classes exist first
                    var parentPath = string.Join(":", parts.Take(parts.Length - 1));
                    if (!_existingClasses.Contains(parentPath))
                    {
                        CreateClassInQB2021(parentPath);
                    }
                }

                if (_importConfig.DryRun)
                {
                    Log.Debug("    [DRY RUN] Would create class: {Class}", className);
                    _existingClasses.Add(className);
                    return true;
                }

                var leafName = parts.Last();
                var parentRef = parts.Length > 1
                    ? $"<ParentRef><FullName>{EscapeXml(string.Join(":", parts.Take(parts.Length - 1)))}</FullName></ParentRef>"
                    : "";

                var requestXml = $@"<ClassAddRq>
  <ClassAdd>
    <Name>{EscapeXml(leafName)}</Name>
    {parentRef}
    <IsActive>true</IsActive>
  </ClassAdd>
</ClassAddRq>";

                var fullRequest = QBConnectionManager.BuildQBXMLRequest(requestXml, _sdkVersion);
                var response = _connection.ProcessRequestWithRetry(fullRequest);
                var doc = XDocument.Parse(response);

                var statusCode = doc.Descendants("ClassAddRs").FirstOrDefault()?.Attribute("statusCode")?.Value;
                if (statusCode == "0" || statusCode == "3100") // 3100 = already exists
                {
                    _existingClasses.Add(className);
                    return true;
                }

                var statusMessage = doc.Descendants("ClassAddRs").FirstOrDefault()?.Attribute("statusMessage")?.Value;
                Log.Warning("    Failed to create class '{Class}': {Message}", className, statusMessage);
                return false;
            }
            catch (Exception ex)
            {
                Log.Warning("    Error creating class '{Class}': {Message}", className, ex.Message);
                return false;
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // COMPANY PREFERENCES / ACCOUNTING MODEL
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Queries company preferences from a QuickBooks company file.
        /// Uses QBXML PreferencesQueryRq to get accounting method, report basis, etc.
        /// </summary>
        public CompanyPreferences QueryCompanyPreferences()
        {
            var prefs = new CompanyPreferences();

            try
            {
                Log.Information("  Querying company preferences...");

                var requestXml = @"<PreferencesQueryRq></PreferencesQueryRq>";
                var fullRequest = QBConnectionManager.BuildQBXMLRequest(requestXml, _sdkVersion);
                var response = _connection.ProcessRequestWithRetry(fullRequest);
                var doc = XDocument.Parse(response);

                var prefsRet = doc.Descendants("PreferencesRet").FirstOrDefault();
                if (prefsRet != null)
                {
                    // Extract accounting preferences
                    var accountingPrefs = prefsRet.Element("AccountingPrefs");
                    if (accountingPrefs != null)
                    {
                        prefs.ReportBasis = accountingPrefs.Element("ReportBasis")?.Value ?? "";
                        prefs.AccountingMethod = prefs.ReportBasis; // In QB, ReportBasis indicates Cash vs Accrual

                        var classTracking = accountingPrefs.Element("IsUsingClassTracking")?.Value;
                        prefs.IsClassTrackingEnabled = classTracking?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;
                    }

                    // Extract other preferences
                    var currentAppPrefs = prefsRet.Element("CurrentAppAccessRights");
                    prefs.IsMultiCurrencyEnabled = prefsRet.Descendants("IsMultiCurrencyOn")
                        .FirstOrDefault()?.Value?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;

                    var fiscalMonth = prefsRet.Descendants("FiscalYearStartMonth")
                        .FirstOrDefault()?.Value;
                    if (int.TryParse(fiscalMonth, out var month))
                        prefs.FiscalYearStartMonth = month;
                }

                Log.Information("    Accounting method: {Method}", prefs.AccountingMethod);
                Log.Information("    Report basis: {Basis}", prefs.ReportBasis);
                Log.Information("    Class tracking enabled: {Enabled}", prefs.IsClassTrackingEnabled);
            }
            catch (Exception ex)
            {
                Log.Warning("    Could not query company preferences: {Message}", ex.Message);
            }

            return prefs;
        }

        /// <summary>
        /// Applies the accounting method (Cash vs Accrual) to QB 2021 via QBXML PreferencesMod.
        /// Note: QuickBooks SDK has limited support for modifying preferences.
        /// The ReportBasis can typically be set through company preferences.
        /// </summary>
        public bool ApplyAccountingPreferences(CompanyPreferences sourcePrefs)
        {
            if (string.IsNullOrEmpty(sourcePrefs.AccountingMethod))
            {
                Log.Warning("  No accounting method specified in source preferences.");
                return false;
            }

            Log.Information("  Applying accounting preferences to QB 2021...");
            Log.Information("    Setting accounting method to: {Method}", sourcePrefs.AccountingMethod);
            Log.Information("    Setting report basis to: {Basis}", sourcePrefs.ReportBasis);

            if (_importConfig.DryRun)
            {
                Log.Information("    [DRY RUN] Would set accounting method to {Method}", sourcePrefs.AccountingMethod);
                return true;
            }

            try
            {
                // Build PreferencesModRq - note that QB SDK support for modifying preferences is limited
                // The ReportBasis is typically modifiable
                var reportBasis = sourcePrefs.ReportBasis;
                if (string.IsNullOrEmpty(reportBasis))
                    reportBasis = sourcePrefs.AccountingMethod;

                var requestXml = $@"<PreferencesModRq>
  <PreferencesMod>
    <AccountingPrefs>
      <ReportBasis>{EscapeXml(reportBasis)}</ReportBasis>
    </AccountingPrefs>
  </PreferencesMod>
</PreferencesModRq>";

                var fullRequest = QBConnectionManager.BuildQBXMLRequest(requestXml, _sdkVersion);
                var response = _connection.ProcessRequestWithRetry(fullRequest);
                var doc = XDocument.Parse(response);

                var statusCode = doc.Descendants("PreferencesModRs")
                    .FirstOrDefault()?.Attribute("statusCode")?.Value;

                if (statusCode == "0")
                {
                    Log.Information("    ✓ Accounting preferences applied successfully");
                    return true;
                }

                var statusMessage = doc.Descendants("PreferencesModRs")
                    .FirstOrDefault()?.Attribute("statusMessage")?.Value;
                Log.Warning("    ⚠ Preferences modification returned status {Code}: {Message}",
                    statusCode, statusMessage);

                // Even if the SDK doesn't support this operation, we log it for manual follow-up
                Log.Information("    NOTE: If preferences cannot be set via SDK, manually set accounting method " +
                    "to '{Method}' in QuickBooks 2021 > Edit > Preferences > Accounting > Company Preferences",
                    sourcePrefs.AccountingMethod);

                return false;
            }
            catch (Exception ex)
            {
                Log.Warning("    Error applying accounting preferences: {Message}", ex.Message);
                Log.Information("    NOTE: Manually set accounting method to '{Method}' in QB 2021",
                    sourcePrefs.AccountingMethod);
                return false;
            }
        }

        /// <summary>
        /// Escapes special XML characters in a string.
        /// </summary>
        private static string EscapeXml(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;

            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }
    }
}
