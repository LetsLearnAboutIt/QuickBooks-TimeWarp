using System.Text;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using QB_TimeWarp.Helpers;
using QB_TimeWarp.Models;
using Serilog;

namespace QB_TimeWarp.Services
{
    /// <summary>
    /// Imports transformed data into QuickBooks 2021 via QBXML SDK.
    /// Handles dependency ordering, ID remapping, error recovery,
    /// SDK version compatibility, and smart error handling for 0x80040400.
    /// </summary>
    public class DataImporter
    {
        private readonly QBConnectionManager _connection;
        private readonly ImportConfig _importConfig;
        private readonly TransformationRulesConfig _transformationRules;
        private readonly string _sdkVersion;
        private string _effectiveSDKVersion;

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
        /// Combined with SDK 16.0-only fields that QB 2021 does not support.
        /// </summary>
        private static readonly HashSet<string> ExcludedFields = new(StringComparer.OrdinalIgnoreCase)
        {
            // Read-only / system-generated fields
            "ListID", "TxnID", "TimeCreated", "TimeModified", "EditSequence",
            "TxnNumber", "Balance", "TotalBalance", "Subtotal", "BalanceRemaining",
            "IsPaid", "ExternalGUID", "FullName", "OpenBalance",
            // SDK 16.0-only fields (cause 0x80040400 in QB 2021)
            "TaxRegistrationNumber", "PreferredDeliveryMethod", "SubscriptionPaymentStatus",
            "DeliveryInfo", "TaxLineRef", "ForceUOMChange", "LinkToTxnID",
            // Payroll fields not supported in simplified QB 2021 format
            "EmployeePayrollInfo", "ClearEarnings", "BillingRateRef",
        };

        /// <summary>
        /// Tracks fields that caused 0x80040400 errors at runtime so we can skip them on retry.
        /// </summary>
        private readonly HashSet<string> _dynamicExcludedFields = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Tracks the number of compatibility-related skips.
        /// </summary>
        private int _incompatibleFieldSkips;
        private int _emptyNameSkips;

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
            _effectiveSDKVersion = QBSDKVersionHelper.IsQB2021Compatible(sdkVersion)
                ? sdkVersion : QBSDKVersionHelper.QB2021_MAX_SDK_VERSION;
            _transformationRules = transformationRules;

            Log.Information("DataImporter initialized: configured SDK={Configured}, effective SDK={Effective}",
                sdkVersion, _effectiveSDKVersion);
        }

        /// <summary>
        /// Sets the effective SDK version (called after version detection).
        /// </summary>
        public void SetEffectiveSDKVersion(string version)
        {
            _effectiveSDKVersion = version;
            Log.Information("DataImporter SDK version updated to: {Version}", version);
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
            if (_incompatibleFieldSkips > 0)
                Log.Information("  {Count} incompatible fields skipped (SDK 15.0 compatibility)", _incompatibleFieldSkips);
            if (_emptyNameSkips > 0)
                Log.Warning("  {Count} entities skipped due to empty names", _emptyNameSkips);
            if (_dynamicExcludedFields.Any())
            {
                Log.Information("  Fields dynamically excluded after 0x80040400 errors: {Fields}",
                    string.Join(", ", _dynamicExcludedFields));
            }
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
        /// Includes smart error handling: if a 0x80040400/3250 error is returned (unsupported field),
        /// the offending field is identified and added to the dynamic exclusion list.
        /// The import is retried ONCE without the problematic field (not 3 times).
        /// </summary>
        private ImportResult ImportSingleEntity(string entityType, QBEntity entity)
        {
            var result = new ImportResult
            {
                EntityType = entityType,
                SourceIdentifier = entity.FullName ?? entity.Name
            };

            // Pre-validation: skip entities with empty names (they'll fail anyway)
            if (string.IsNullOrWhiteSpace(entity.Name) && string.IsNullOrWhiteSpace(entity.FullName))
            {
                result.Success = false;
                result.ErrorMessage = "Entity has empty Name and FullName — cannot import.";
                _emptyNameSkips++;
                return result;
            }

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

                // Send to QuickBooks — use ProcessRequest directly (not WithRetry)
                // for the first attempt so we can handle version errors ourselves
                string response;
                try
                {
                    response = _connection.ProcessRequest(requestXml);
                }
                catch (QBRequestException ex) when (IsVersionCompatibilityError(ex))
                {
                    // This is likely a 0x80040400 — try to identify the problematic field
                    Log.Warning("  SDK compatibility error for '{Name}': {Message}. Attempting field-reduced retry...",
                        entity.Name, ex.Message);

                    // Add likely problematic fields to exclusion list
                    IdentifyAndExcludeProblematicFields(entity, entityType);

                    // Retry with reduced fields (single retry, not 3)
                    var retryXml = BuildAddRequestXml(entityType, entity);
                    if (string.IsNullOrEmpty(retryXml))
                    {
                        result.Success = false;
                        result.ErrorMessage = $"Retry failed: could not build reduced QBXML. Original error: {ex.Message}";
                        return result;
                    }

                    response = _connection.ProcessRequest(retryXml);
                }

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
                    else if (QBSDKVersionHelper.IsUnsupportedFieldError(statusCode))
                    {
                        // This is a version-compatibility error — log and learn
                        result.Success = false;
                        result.ErrorCode = statusCode;
                        result.ErrorMessage = $"{statusMessage} [{QBSDKVersionHelper.ExplainErrorCode(statusCode)}]";

                        LearnFromErrorResponse(statusMessage, entityType);
                        _incompatibleFieldSkips++;
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
        /// Checks if an exception is related to SDK version compatibility (0x80040400).
        /// </summary>
        private static bool IsVersionCompatibilityError(Exception ex)
        {
            if (ex is System.Runtime.InteropServices.COMException comEx)
            {
                return QBSDKVersionHelper.IsUnsupportedElementCOMError(comEx.HResult);
            }

            // Check the message for common indicators
            var msg = ex.Message.ToUpperInvariant();
            return msg.Contains("80040400") || msg.Contains("UNSUPPORTED") ||
                   msg.Contains("NOT VALID") || msg.Contains("ELEMENT NOT");
        }

        /// <summary>
        /// Attempts to identify which fields might be causing compatibility errors
        /// and adds them to the dynamic exclusion list.
        /// </summary>
        private void IdentifyAndExcludeProblematicFields(QBEntity entity, string entityType)
        {
            // Check for SDK 16-only fields that might have slipped through
            foreach (var prop in entity.Fields.Properties())
            {
                if (QBSDKVersionHelper.SDK16OnlyFields.Contains(prop.Name) &&
                    !_dynamicExcludedFields.Contains(prop.Name))
                {
                    _dynamicExcludedFields.Add(prop.Name);
                    Log.Warning("  Dynamically excluding field '{Field}' from {EntityType} imports",
                        prop.Name, entityType);
                }

                // Also check for payroll fields
                if (QBSDKVersionHelper.IsPayrollFieldToSimplify(prop.Name) &&
                    !_dynamicExcludedFields.Contains(prop.Name))
                {
                    _dynamicExcludedFields.Add(prop.Name);
                    Log.Warning("  Dynamically excluding payroll field '{Field}' from {EntityType} imports",
                        prop.Name, entityType);
                }
            }
        }

        /// <summary>
        /// Learns from QB error responses to improve future imports.
        /// Parses the error message to identify field names that QB doesn't support.
        /// </summary>
        private void LearnFromErrorResponse(string? statusMessage, string entityType)
        {
            if (string.IsNullOrEmpty(statusMessage)) return;

            // QB error messages often mention the offending element name
            // e.g., "The element 'ExchangeRate' is not valid for this request"
            var msg = statusMessage;

            // Try to extract field name from common error patterns
            var patterns = new[]
            {
                "element '", "field '", "tag '",
                "Element '", "Field '", "Tag '"
            };

            foreach (var pattern in patterns)
            {
                var idx = msg.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    var start = idx + pattern.Length;
                    var end = msg.IndexOf("'", start);
                    if (end > start)
                    {
                        var fieldName = msg.Substring(start, end - start);
                        if (!_dynamicExcludedFields.Contains(fieldName))
                        {
                            _dynamicExcludedFields.Add(fieldName);
                            Log.Warning("  Learned: field '{Field}' is not supported in QB 2021 for {EntityType}. " +
                                "Will exclude from future imports.", fieldName, entityType);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Validates a sample of records before full import to detect compatibility issues early.
        /// Returns a list of validation issues found.
        /// </summary>
        public List<string> ValidateSampleBeforeImport(Dictionary<string, ExportedEntitySet> transformedData, int sampleSize = 3)
        {
            var issues = new List<string>();

            Log.Information("════════════════════════════════════════════════════");
            Log.Information("  PRE-IMPORT VALIDATION (sample of {SampleSize} per type)", sampleSize);
            Log.Information("════════════════════════════════════════════════════");

            foreach (var (entityType, entitySet) in transformedData)
            {
                if (entityType == "Preferences" || entityType == "CompanyInfo") continue;
                if (entitySet.TotalCount == 0) continue;

                var sample = entitySet.Entities.Take(sampleSize).ToList();
                var entityIssues = new List<string>();

                foreach (var entity in sample)
                {
                    // Check for empty names
                    if (string.IsNullOrWhiteSpace(entity.Name) && string.IsNullOrWhiteSpace(entity.FullName))
                    {
                        entityIssues.Add($"[{entityType}] Entity has empty Name (ListID={entity.ListID})");
                    }

                    // Check for SDK 16-only fields
                    foreach (var prop in entity.Fields.Properties())
                    {
                        if (QBSDKVersionHelper.SDK16OnlyFields.Contains(prop.Name))
                        {
                            entityIssues.Add($"[{entityType}] Contains SDK 16-only field: {prop.Name}");
                        }
                    }

                    // Check field lengths against QB 2021 limits
                    foreach (var prop in entity.Fields.Properties())
                    {
                        if (prop.Value.Type != JTokenType.String) continue;
                        var maxLen = QBSDKVersionHelper.GetQB2021MaxLength(prop.Name);
                        if (maxLen.HasValue && prop.Value.ToString().Length > maxLen.Value)
                        {
                            entityIssues.Add($"[{entityType}] Field {prop.Name} exceeds QB 2021 limit " +
                                $"({prop.Value.ToString().Length} > {maxLen.Value})");
                        }
                    }

                    // Try building the QBXML request to validate
                    try
                    {
                        var xml = BuildAddRequestXml(entityType, entity);
                        if (string.IsNullOrEmpty(xml))
                        {
                            entityIssues.Add($"[{entityType}] Could not build QBXML for '{entity.Name}'");
                        }
                    }
                    catch (Exception ex)
                    {
                        entityIssues.Add($"[{entityType}] QBXML build error for '{entity.Name}': {ex.Message}");
                    }
                }

                if (entityIssues.Any())
                {
                    Log.Warning("  {EntityType}: {Count} issues in sample", entityType, entityIssues.Count);
                    foreach (var issue in entityIssues)
                    {
                        Log.Warning("    {Issue}", issue);
                        issues.Add(issue);
                    }
                }
                else
                {
                    Log.Information("  {EntityType}: ✓ sample validated OK", entityType);
                }
            }

            if (issues.Any())
            {
                Log.Warning("  VALIDATION: {Count} issues found. Review before proceeding with full import.", issues.Count);
            }
            else
            {
                Log.Information("  VALIDATION: ✓ All samples passed. Ready for full import.");
            }

            return issues;
        }

        /// <summary>
        /// Builds the QBXML Add request XML for a single entity.
        /// Uses the effective SDK version for QB 2021 compatibility.
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

            return QBConnectionManager.BuildQBXMLRequest(innerXml.ToString(), _effectiveSDKVersion);
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

            return QBConnectionManager.BuildQBXMLRequest(innerXml.ToString(), _effectiveSDKVersion);
        }

        /// <summary>
        /// Converts a JObject of fields to QBXML element format.
        /// Handles nested objects (Address, Ref types, etc.).
        /// Filters out excluded fields (static + dynamically learned) and validates field lengths.
        /// </summary>
        private string BuildFieldsXml(JObject fields)
        {
            var sb = new StringBuilder();

            foreach (var prop in fields.Properties())
            {
                // Skip static excluded fields
                if (ExcludedFields.Contains(prop.Name))
                    continue;

                // Skip dynamically excluded fields (learned from 0x80040400 errors)
                if (_dynamicExcludedFields.Contains(prop.Name))
                {
                    _incompatibleFieldSkips++;
                    continue;
                }

                // Skip SDK 16-only fields that may have slipped through transformation
                if (QBSDKVersionHelper.SDK16OnlyFields.Contains(prop.Name))
                {
                    _incompatibleFieldSkips++;
                    Log.Debug("  Skipping SDK 16-only field '{Field}' in QBXML generation", prop.Name);
                    continue;
                }

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
                            // Skip excluded child fields
                            if (ExcludedFields.Contains(childProp.Name) ||
                                _dynamicExcludedFields.Contains(childProp.Name))
                                continue;

                            if (childProp.Value.Type != JTokenType.Null &&
                                !string.IsNullOrEmpty(childProp.Value.ToString()))
                            {
                                // Enforce QB 2021 field length limits
                                var childValue = EnforceFieldLength(childProp.Name, childProp.Value.ToString());
                                sb.AppendLine($"      <{childProp.Name}>{EscapeXml(childValue)}</{childProp.Name}>");
                            }
                        }
                        sb.AppendLine($"    </{prop.Name}>");
                    }
                }
                else if (prop.Value.Type != JTokenType.Null && !string.IsNullOrEmpty(prop.Value.ToString()))
                {
                    // Enforce QB 2021 field length limits
                    var value = EnforceFieldLength(prop.Name, prop.Value.ToString());
                    sb.AppendLine($"    <{prop.Name}>{EscapeXml(value)}</{prop.Name}>");
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Enforces QB 2021 field length limits on a value.
        /// Returns the truncated value if it exceeds the limit.
        /// </summary>
        private static string EnforceFieldLength(string fieldName, string value)
        {
            var maxLen = QBSDKVersionHelper.GetQB2021MaxLength(fieldName);
            if (maxLen.HasValue && value.Length > maxLen.Value)
            {
                return value.Substring(0, maxLen.Value);
            }
            return value;
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

                var fullRequest = QBConnectionManager.BuildQBXMLRequest(requestXml, _effectiveSDKVersion);
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

                var fullRequest = QBConnectionManager.BuildQBXMLRequest(requestXml, _effectiveSDKVersion);
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
                var fullRequest = QBConnectionManager.BuildQBXMLRequest(requestXml, _effectiveSDKVersion);
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

                var fullRequest = QBConnectionManager.BuildQBXMLRequest(requestXml, _effectiveSDKVersion);
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
