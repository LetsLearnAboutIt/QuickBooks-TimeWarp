using System.Text;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using QB_TimeWarp.Helpers;
using QB_TimeWarp.Models;
using Serilog;
using static QB_TimeWarp.Helpers.DependencyAnalyzer;

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
        /// Transaction entity types — these use TxnID instead of Name/FullName.
        /// They should NOT be skipped for having empty names.
        /// </summary>
        private static readonly HashSet<string> TransactionEntityTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "Invoices", "Bills", "Payments", "SalesReceipts", "PurchaseOrders",
            "JournalEntries", "CreditMemos", "Estimates", "Deposits",
            "Checks", "VendorCredits", "InventoryAdjustments", "Transfers"
        };

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
            // ── FIX #9: ItemSalesTax as a top-level entity type ──────────────
            // Sales-tax items (e.g. "WI SALES & EXPO") were previously only
            // reachable through ItemSubTypeMap during generic Items import.
            // Now they are exported and imported as a first-class entity type
            // in Stage 1 (Foundation), so Customers / SalesReceipts that
            // reference ItemSalesTaxRef have a valid target in QB 2021.
            ["ItemSalesTax"]    = ("ItemSalesTaxAddRq",  "ItemSalesTaxAdd",  "ItemSalesTaxRet"),
            ["ShipMethods"]     = ("ShipMethodAddRq",    "ShipMethodAdd",    "ShipMethodRet"),
            ["CustomerTypes"]   = ("CustomerTypeAddRq",  "CustomerTypeAdd",  "CustomerTypeRet"),
            ["VendorTypes"]     = ("VendorTypeAddRq",    "VendorTypeAdd",    "VendorTypeRet"),
            ["JobTypes"]        = ("JobTypeAddRq",       "JobTypeAdd",       "JobTypeRet"),
            ["PriceLevels"]     = ("PriceLevelAddRq",    "PriceLevelAdd",    "PriceLevelRet"),
            // FIX #10: CustomerMsgs (e.g. "Thank you for your business!") referenced
            //          from Invoice/SalesReceipt/CreditMemo CustomerMsgRef. Added so the
            //          schema-driven pre-creation loop can call BuildSimpleListAddRequest
            //          for them and so the generic import path can find the Add request
            //          type when CustomerMsgs are exported as a top-level entity set.
            ["CustomerMsgs"]    = ("CustomerMsgAddRq",   "CustomerMsgAdd",   "CustomerMsgRet"),

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
            // FIX #8: TxnLineID is read-only — present on every *LineRet from export
            // but is NEVER valid inside a *LineAdd request (causes 0x80040400 XML parse failure).
            "TxnLineID",
            // SDK 16.0-only fields (cause 0x80040400 in QB 2021)
            "TaxRegistrationNumber", "PreferredDeliveryMethod", "SubscriptionPaymentStatus",
            "DeliveryInfo", "TaxLineRef", "ForceUOMChange", "LinkToTxnID",
            // Payroll fields not supported in simplified QB 2021 format
            "EmployeePayrollInfo", "ClearEarnings", "BillingRateRef",
            // ══════════════════════════════════════════════════════════════
            // FIX #12: Response-only fields that appear in *Ret but are
            // NEVER valid in any *Add request. Including them causes
            // 0x80040400 "error parsing XML text stream".
            // ══════════════════════════════════════════════════════════════
            "AddressBlock",          // Formatted address block — response-only
            "IsPending",             // Not in any *Add schema
            "LinkedTxn",             // Linked transaction info — response-only
            "DataExtRet",            // Custom field data — response-only format
        };

        // ═══════════════════════════════════════════════════════════════════
        // FIX #12: Fields to exclude from line items in *Add requests
        // ═══════════════════════════════════════════════════════════════════
        // These fields appear in *LineRet responses from the export but are
        // NOT valid inside *LineAdd requests. Including them causes
        // 0x80040400 XML parse errors in QB 2021.
        private static readonly HashSet<string> LineItemExcludedFields = new(StringComparer.OrdinalIgnoreCase)
        {
            "TxnLineID",           // Read-only line identifier
            "SeqNum",              // Read-only sequence number
            "LinkedTxn",           // Read-only linked transaction info
            "TxnType",            // Response-only field on DepositLineRet — not valid in DepositLineAdd
        };

        // ═══════════════════════════════════════════════════════════════════
        // FIX #8: Per-transaction-type header-field exclusions
        // ═══════════════════════════════════════════════════════════════════
        //
        // ROOT CAUSE: 1,634 / 1,634 transactions failed with
        //   "QuickBooks found an error when parsing the provided XML text stream"
        // because the *Add request XML contained fields that are NOT valid in
        // the corresponding QBXML XSD schema. Two culprits:
        //
        //   1. <Name>  — Never a valid child of any transaction *Add element.
        //      DataTransformer.FixEmptyNameField synthesises a placeholder
        //      "Name" into entity.Fields when the source QBEntity had no Name
        //      (because transactions identify themselves with TxnID, not Name).
        //      That placeholder must be stripped on the way out.
        //
        //   2. <Amount> at header level for Checks / JournalEntries /
        //      Deposits / SalesReceipts. The CheckRet response contains
        //      a header <Amount> (the rolled-up total), but CheckAdd does
        //      NOT — the amount belongs inside each <ExpenseLineAdd>.
        //      Same story for the other three. TransferAdd is the exception:
        //      it IS header-only, so its <Amount> stays at header.
        //
        //   3. <FromAccountBalance> / <ToAccountBalance> — TransferRet
        //      returns these read-only balances; TransferAdd does not
        //      accept them.
        //
        //   4. Various computed totals (TotalAmount, AppliedAmount,
        //      BalanceRemaining, etc.) returned in *Ret but rejected by
        //      *Add — preserved here as defensive exclusions for the
        //      remaining transaction types so a future run does not
        //      regress them.
        //
        // The map is keyed by collection name (matches AddMap keys and
        // ImportStage.EntityTypes). HashSet is case-insensitive so callers
        // do not have to normalise.
        //
        private static readonly Dictionary<string, HashSet<string>> TransactionHeaderExcludedFields
            = new(StringComparer.OrdinalIgnoreCase)
        {
            // ── The 5 transaction types in scope for FIX #8 ──────────────
            ["Checks"] = new(StringComparer.OrdinalIgnoreCase)
            {
                "Name",              // not in CheckAdd schema
                "Amount",            // belongs on <ExpenseLineAdd><Amount>
            },
            ["JournalEntries"] = new(StringComparer.OrdinalIgnoreCase)
            {
                "Name",              // not in JournalEntryAdd schema
                "Amount",            // belongs on each JournalDebitLine/JournalCreditLine
                "TotalAmount",       // computed rollup
                "DebitTotal",        // computed rollup
                "CreditTotal",       // computed rollup
            },
            ["Deposits"] = new(StringComparer.OrdinalIgnoreCase)
            {
                "Name",              // not in DepositAdd schema
                "Amount",            // belongs on <DepositLineAdd><Amount>
                "DepositTotal",      // computed rollup
                "TotalDeposit",      // computed rollup
            },
            ["SalesReceipts"] = new(StringComparer.OrdinalIgnoreCase)
            {
                "Name",              // not in SalesReceiptAdd schema
                "Amount",            // belongs on <SalesReceiptLineAdd><Amount>
                "TotalAmount",       // computed rollup
                "AppliedAmount",     // computed rollup
            },
            ["Transfers"] = new(StringComparer.OrdinalIgnoreCase)
            {
                "Name",                // not in TransferAdd schema
                "FromAccountBalance",  // read-only on TransferRet
                "ToAccountBalance",    // read-only on TransferRet
                // NOTE: Amount stays at header level for Transfers — it IS in TransferAddOrder.
            },

            // ── Defensive exclusions for the other transaction types ─────
            // (they were not in the failing-1,634 cohort but FixEmptyNameField
            //  will inject "Name" into them too, and the *Add schemas all
            //  reject computed rollups returned by their *Ret counterparts).
            ["Bills"] = new(StringComparer.OrdinalIgnoreCase)
            {
                "Name", "AmountDue", "AppliedAmount",
            },
            ["Invoices"] = new(StringComparer.OrdinalIgnoreCase)
            {
                "Name", "AppliedAmount", "BalanceRemaining",
            },
            ["CreditMemos"] = new(StringComparer.OrdinalIgnoreCase)
            {
                "Name", "AppliedAmount", "TotalAmount", "CreditRemaining",
            },
            ["Estimates"] = new(StringComparer.OrdinalIgnoreCase)
            {
                "Name", "TotalAmount",
            },
            ["PurchaseOrders"] = new(StringComparer.OrdinalIgnoreCase)
            {
                "Name", "TotalAmount", "ReceivedAmount",
            },
            ["VendorCredits"] = new(StringComparer.OrdinalIgnoreCase)
            {
                "Name", "AppliedAmount", "CreditRemaining",
            },
            ["Payments"] = new(StringComparer.OrdinalIgnoreCase)
            {
                "Name", "UnusedPayment", "UnusedCredits",
            },
            ["InventoryAdjustments"] = new(StringComparer.OrdinalIgnoreCase)
            {
                "Name",
            },

            // ═══════════════════════════════════════════════════════════════════
            // FIX #16: Employee — exclude combined "Name" field
            // ═══════════════════════════════════════════════════════════════════
            // QB 2021 EmployeeAdd does NOT accept a combined <Name> field when
            // separate <FirstName>, <MiddleName>, <LastName> are provided.
            // Including both causes "error parsing the provided XML text stream"
            // (0x80040400). The exported data already has the split fields, so
            // we suppress the combined Name here and let QB 2021 auto-generate
            // the list display name from the split components.
            // Also suppress FullName (response-only) for safety.
            // ═══════════════════════════════════════════════════════════════════
            ["Employees"] = new(StringComparer.OrdinalIgnoreCase)
            {
                "Name",              // combined display name — not valid with split FirstName/LastName
                "FullName",          // response-only (same as Name for non-hierarchical entities)
            },
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
        private int _alreadyExistsCount;

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
            if (_alreadyExistsCount > 0)
                Log.Information("  {Count} entities already existed in target (treated as success)", _alreadyExistsCount);
            if (_dynamicExcludedFields.Any())
            {
                Log.Information("  Fields dynamically excluded after 0x80040400 errors: {Fields}",
                    string.Join(", ", _dynamicExcludedFields));
            }
            Log.Information("═══════════════════════════════════════════════════════");

            return report;
        }

        // ═══════════════════════════════════════════════════════════════════
        // STAGED IMPORT — Dependency-aware multi-stage import
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// The 4 import stages, in dependency order.
        /// Stage 1 must complete before Stage 2, etc.
        /// </summary>
        private static readonly List<ImportStage> ImportStages = new()
        {
            new ImportStage
            {
                StageNumber = 1,
                StageName = "Foundation",
                Description = "Chart of Accounts, Sales Tax, Payment Terms, Classes, and other lookup items",
                IsCritical = true,
                EntityTypes = new List<string>
                {
                    "Accounts", "SalesTaxCodes",
                    // FIX #9: ItemSalesTax belongs in Stage 1 — Customers and
                    // SalesReceipts in later stages reference ItemSalesTaxRef
                    // and need the target sales-tax items to exist first.
                    "ItemSalesTax",
                    "Terms", "PaymentMethods",
                    "CustomerTypes", "VendorTypes", "JobTypes", "Classes",
                    "ShipMethods", "PriceLevels"
                }
            },
            new ImportStage
            {
                StageNumber = 2,
                StageName = "Entities",
                Description = "Customers, Vendors, Employees, and Other Names",
                IsCritical = true,
                EntityTypes = new List<string>
                {
                    "Customers", "Vendors", "Employees"
                }
            },
            new ImportStage
            {
                StageNumber = 3,
                StageName = "Items",
                Description = "Service Items, Inventory Items, Non-Inventory Items, Item Groups",
                IsCritical = true,
                EntityTypes = new List<string>
                {
                    "Items"
                }
            },
            new ImportStage
            {
                StageNumber = 4,
                StageName = "Transactions",
                Description = "Invoices, Bills, Checks, Deposits, Journal Entries, Credit Memos, Sales Receipts",
                IsCritical = false,
                EntityTypes = new List<string>
                {
                    "Invoices", "Bills", "Payments", "SalesReceipts", "PurchaseOrders",
                    "JournalEntries", "CreditMemos", "Estimates", "Deposits",
                    "Checks", "VendorCredits", "InventoryAdjustments", "Transfers"
                }
            }
        };

        /// <summary>
        /// Performs a dependency analysis on the transformed data.
        /// Returns information about all referenced items and what's missing.
        /// </summary>
        public DependencyAnalysisResult AnalyzeDependencies(Dictionary<string, ExportedEntitySet> transformedData)
        {
            var analyzer = new DependencyAnalyzer();
            return analyzer.Analyze(transformedData);
        }

        /// <summary>
        /// Imports data using a staged approach with dependency analysis.
        /// This is the preferred import method — ensures foundation items exist
        /// before entities, entities before items, items before transactions.
        /// 
        /// Replaces the flat ImportAll() for production use.
        /// </summary>
        public StagedImportSummary ImportDataInStages(Dictionary<string, ExportedEntitySet> transformedData)
        {
            var summary = new StagedImportSummary
            {
                StartTime = DateTime.UtcNow
            };

            Log.Information("╔═══════════════════════════════════════════════════════╗");
            Log.Information("║  STAGED IMPORT — Dependency-Aware Multi-Stage Import  ║");
            Log.Information("╚═══════════════════════════════════════════════════════╝");

            if (_importConfig.DryRun)
            {
                Log.Warning("*** DRY RUN MODE — No data will actually be imported ***");
            }

            // ── FIX #3: Stage 0 — Verify Account Types in QB 2021 ───
            Log.Information("");
            Log.Information("  ┌─────────────────────────────────────────┐");
            Log.Information("  │  Stage 0: Verify Account Types (FIX #3) │");
            Log.Information("  └─────────────────────────────────────────┘");

            VerifyAccountTypesExist();

            // ── Pre-Analysis Phase ──────────────────────────────────
            Log.Information("");
            Log.Information("  ┌──────────────────────────────────────────────┐");
            Log.Information("  │  Phase 0b: Pre-Import Dependency Analysis     │");
            Log.Information("  └──────────────────────────────────────────────┘");

            var dependencies = AnalyzeDependencies(transformedData);

            // ── FIX #4: Create missing reference types BEFORE Stage 1 ──
            Log.Information("");
            Log.Information("  ┌──────────────────────────────────────────────────────┐");
            Log.Information("  │  Phase 0c: Create Missing Reference Types (FIX #4)    │");
            Log.Information("  └──────────────────────────────────────────────────────┘");

            CreateMissingReferenceTypes(dependencies, summary);

            // ── Auto-create missing foundation items ─────────────────
            if (_importConfig.AutoCreateMissingDependencies && dependencies.MissingItems.Any())
            {
                Log.Information("");
                Log.Information("  Auto-creating remaining missing dependencies...");
                AutoCreateMissingItems(dependencies, summary);
            }

            // ── Execute Stages ───────────────────────────────────────
            var stages = GetEnabledStages();

            foreach (var stage in stages)
            {
                Log.Information("");
                Log.Information("  ┌─────────────────────────────────────────┐");
                Log.Information("  │  Stage {StageNum}/4: {StageName,-33} │",
                    stage.StageNumber, stage.StageName);
                Log.Information("  │  {Desc,-41} │", stage.Description.Length > 41
                    ? stage.Description[..38] + "..."
                    : stage.Description);
                Log.Information("  └─────────────────────────────────────────┘");

                var stageSummary = ExecuteStage(stage, transformedData, dependencies);
                summary.Stages.Add(stageSummary);

                // Report stage results
                Log.Information("  Stage {Num} Result: {Succeeded}/{Attempted} succeeded, {Failed} failed ({Duration:F1}s)",
                    stage.StageNumber,
                    stageSummary.TotalSucceeded,
                    stageSummary.TotalAttempted,
                    stageSummary.TotalFailed,
                    stageSummary.Duration.TotalSeconds);

                if (stageSummary.AutoCreatedItems > 0)
                {
                    Log.Information("    Auto-created {Count} missing items", stageSummary.AutoCreatedItems);
                }

                // ── Validation Between Stages ────────────────────────
                if (_importConfig.ValidateBetweenStages && !stageSummary.Passed)
                {
                    if (stage.IsCritical && _importConfig.HaltOnFoundationFailure)
                    {
                        Log.Error("  ✗ STAGE {Num} ({Name}) FAILED — Halting import. Reason: {Reason}",
                            stage.StageNumber, stage.StageName, stageSummary.FailureReason);
                        summary.HaltedAtStage = $"Stage {stage.StageNumber}: {stage.StageName}";
                        break;
                    }
                    else
                    {
                        Log.Warning("  ⚠ Stage {Num} ({Name}) had issues but is not critical. Continuing...",
                            stage.StageNumber, stage.StageName);
                    }
                }
            }

            summary.EndTime = DateTime.UtcNow;

            // ── Final Summary ────────────────────────────────────────
            Log.Information("");
            Log.Information("╔═══════════════════════════════════════════════════════╗");
            Log.Information("║  STAGED IMPORT COMPLETE                               ║");
            Log.Information("╚═══════════════════════════════════════════════════════╝");
            Log.Information("  Stages completed: {Completed}/{Total}",
                summary.StagesCompleted, summary.TotalStages);
            Log.Information("  Total records: {Succeeded}/{Attempted} succeeded",
                summary.TotalRecordsSucceeded, summary.TotalRecordsAttempted);
            if (summary.TotalRecordsFailed > 0)
                Log.Warning("  Failed records: {Failed}", summary.TotalRecordsFailed);
            if (summary.TotalAutoCreated > 0)
                Log.Information("  Auto-created items: {Count}", summary.TotalAutoCreated);
            if (!string.IsNullOrEmpty(summary.HaltedAtStage))
                Log.Error("  Import halted at: {Stage}", summary.HaltedAtStage);
            Log.Information("  Total duration: {Duration:F1}s", summary.TotalDuration.TotalSeconds);

            if (_incompatibleFieldSkips > 0)
                Log.Information("  Incompatible fields skipped: {Count}", _incompatibleFieldSkips);
            if (_dynamicExcludedFields.Any())
                Log.Information("  Fields dynamically excluded: {Fields}",
                    string.Join(", ", _dynamicExcludedFields));

            return summary;
        }

        /// <summary>
        /// Converts a StagedImportSummary to a MigrationReport for backward compatibility.
        /// </summary>
        public MigrationReport ConvertToMigrationReport(StagedImportSummary staged)
        {
            var report = new MigrationReport
            {
                StartTime = staged.StartTime,
                EndTime = staged.EndTime,
                SourceCompanyFile = "QB 2023",
                TargetCompanyFile = "QB 2021"
            };

            foreach (var stage in staged.Stages)
            {
                foreach (var (entityType, batchSummary) in stage.EntitySummaries)
                {
                    report.EntitySummaries[entityType] = batchSummary;
                }
            }

            report.OverallStatus = staged.AllStagesPassed
                ? MigrationStatus.Completed
                : staged.TotalRecordsSucceeded > 0
                    ? MigrationStatus.CompletedWithErrors
                    : MigrationStatus.Failed;

            return report;
        }

        /// <summary>
        /// Gets the list of enabled import stages based on configuration.
        /// </summary>
        private List<ImportStage> GetEnabledStages()
        {
            var enabled = new List<ImportStage>();
            var stageConfig = _importConfig.Stages;

            foreach (var stage in ImportStages)
            {
                bool isEnabled = stage.StageNumber switch
                {
                    1 => stageConfig.Stage1_Foundation,
                    2 => stageConfig.Stage2_Entities,
                    3 => stageConfig.Stage3_Items,
                    4 => stageConfig.Stage4_Transactions,
                    _ => true
                };

                if (isEnabled)
                    enabled.Add(stage);
                else
                    Log.Information("  Stage {Num} ({Name}) is DISABLED in configuration.",
                        stage.StageNumber, stage.StageName);
            }

            return enabled;
        }

        /// <summary>
        /// Executes a single import stage: imports all entity types in the stage.
        /// </summary>
        private StageSummary ExecuteStage(
            ImportStage stage,
            Dictionary<string, ExportedEntitySet> transformedData,
            DependencyAnalysisResult dependencies)
        {
            var stageSummary = new StageSummary
            {
                StageNumber = stage.StageNumber,
                StageName = stage.StageName,
                StartTime = DateTime.UtcNow
            };

            int entityIndex = 0;
            int totalEntitiesInStage = stage.EntityTypes.Count;

            foreach (var entityType in stage.EntityTypes)
            {
                entityIndex++;

                if (!transformedData.ContainsKey(entityType))
                {
                    Log.Debug("  [{Index}/{Total}] No data for {EntityType}, skipping.",
                        entityIndex, totalEntitiesInStage, entityType);
                    continue;
                }

                var entitySet = transformedData[entityType];
                if (entitySet.TotalCount == 0)
                {
                    Log.Debug("  [{Index}/{Total}] {EntityType} has 0 records, skipping.",
                        entityIndex, totalEntitiesInStage, entityType);
                    continue;
                }

                Log.Information("  [{Index}/{Total}] Importing {EntityType} ({Count} records)...",
                    entityIndex, totalEntitiesInStage, entityType, entitySet.TotalCount);

                // Resolve references before importing this entity type
                ResolveReferences(entityType, entitySet, dependencies);

                var batchSummary = ImportEntitySet(entityType, entitySet);
                stageSummary.EntitySummaries[entityType] = batchSummary;
                stageSummary.TotalAttempted += batchSummary.TotalAttempted;
                stageSummary.TotalSucceeded += batchSummary.Succeeded;
                stageSummary.TotalFailed += batchSummary.Failed;
                stageSummary.TotalSkipped += batchSummary.Skipped;

                Log.Information("    → {Succeeded} succeeded, {Failed} failed, {Skipped} skipped ({Duration:F1}s)",
                    batchSummary.Succeeded, batchSummary.Failed, batchSummary.Skipped,
                    batchSummary.Duration.TotalSeconds);
            }

            stageSummary.EndTime = DateTime.UtcNow;

            // Determine if stage passed
            if (stageSummary.TotalAttempted == 0)
            {
                stageSummary.Passed = true; // No data = pass
            }
            else if (stageSummary.TotalFailed == 0)
            {
                stageSummary.Passed = true;
            }
            else if (stage.IsCritical && stageSummary.TotalSucceeded == 0)
            {
                stageSummary.Passed = false;
                stageSummary.FailureReason = $"All {stageSummary.TotalFailed} records failed in critical stage.";
            }
            else
            {
                // Some failures but some successes — pass with warnings
                stageSummary.Passed = true;
                if (stageSummary.TotalFailed > 0)
                {
                    Log.Warning("  Stage {Num}: {Failed}/{Attempted} records failed but stage continues.",
                        stage.StageNumber, stageSummary.TotalFailed, stageSummary.TotalAttempted);
                }
            }

            return stageSummary;
        }

        /// <summary>
        /// Resolves entity references for a given entity type by looking up
        /// ListIDs in the name-to-ListID map (populated as items are imported).
        /// This ensures that later-stage entities reference the correct QB 2021 ListIDs.
        /// </summary>
        private void ResolveReferences(string entityType, ExportedEntitySet entitySet, DependencyAnalysisResult dependencies)
        {
            int resolved = 0;

            foreach (var entity in entitySet.Entities)
            {
                resolved += ResolveFieldReferences(entity.Fields);

                foreach (var lineItem in entity.LineItems)
                {
                    resolved += ResolveFieldReferences(lineItem);
                }
            }

            if (resolved > 0)
            {
                Log.Debug("    Resolved {Count} references for {EntityType}", resolved, entityType);
            }
        }

        /// <summary>
        /// Walks a JObject looking for Ref fields and resolves them to QB 2021 ListIDs
        /// where we have a mapping from a previous stage's import.
        /// </summary>
        private int ResolveFieldReferences(JObject fields)
        {
            int resolved = 0;

            foreach (var prop in fields.Properties().ToList())
            {
                if (!prop.Name.EndsWith("Ref")) continue;
                if (prop.Value is not JObject refObj) continue;

                var fullName = refObj["FullName"]?.ToString();
                if (string.IsNullOrEmpty(fullName)) continue;

                // Determine the entity type this ref points to
                string? refEntityType = GetEntityTypeForRefField(prop.Name);
                if (refEntityType == null) continue;

                // Look up in our ID mappings
                var mapKey = $"{refEntityType}:{fullName}";
                if (_nameToListIdMap.TryGetValue(mapKey, out var listId))
                {
                    // We have a ListID — add it to the ref object for QB 2021
                    refObj["ListID"] = listId;
                    resolved++;
                    Log.Debug("      Resolved {RefField} '{Name}' → ListID {ListID}",
                        prop.Name, fullName, listId);
                }
            }

            return resolved;
        }

        /// <summary>
        /// Maps a reference field name to the entity type it points to.
        /// </summary>
        private static string? GetEntityTypeForRefField(string refFieldName)
        {
            return refFieldName switch
            {
                "AccountRef" or "ARAccountRef" or "APAccountRef" or
                "IncomeAccountRef" or "COGSAccountRef" or "AssetAccountRef" or
                "ExpenseAccountRef" or "DepositToAccountRef" or "BankAccountRef" => "Accounts",
                "SalesTaxCodeRef" => "SalesTaxCodes",
                "ItemSalesTaxRef" => "Items",  // Sales tax items are under Items
                "TermsRef" => "Terms",
                "PaymentMethodRef" or "PreferredPaymentMethodRef" => "PaymentMethods",
                "ClassRef" => "Classes",
                "CustomerRef" => "Customers",
                "VendorRef" => "Vendors",
                "CustomerTypeRef" => "CustomerTypes",
                "VendorTypeRef" => "VendorTypes",
                "JobTypeRef" => "JobTypes",
                "ShipMethodRef" => "ShipMethods",
                "PriceLevelRef" => "PriceLevels",
                "ItemRef" => "Items",
                "EntityRef" => "Customers", // Usually a customer
                _ => null
            };
        }

        // ═══════════════════════════════════════════════════════════════════
        // FIX #3: Stage 0 — Account Type Verification
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// FIX #3: Verifies that QB 2021 has all required account types before import.
        /// Queries the target company file and logs which account types are available.
        /// Fails gracefully if critical types are missing (logs warning, doesn't halt).
        /// </summary>
        private void VerifyAccountTypesExist()
        {
            // All account types that QB 2021 should support
            var requiredAccountTypes = new[]
            {
                "Bank", "AccountsReceivable", "AccountsPayable",
                "OtherCurrentAsset", "FixedAsset", "OtherAsset",
                "OtherCurrentLiability", "LongTermLiability", "Equity",
                "Income", "CostOfGoodsSold", "Expense", "OtherIncome", "OtherExpense"
            };

            var criticalTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Bank", "AccountsReceivable", "AccountsPayable", "Income", "Expense", "Equity"
            };

            try
            {
                Log.Information("  Querying QB 2021 for existing account types...");

                var requestXml = @"<AccountQueryRq><ActiveStatus>All</ActiveStatus></AccountQueryRq>";
                var fullRequest = QBConnectionManager.BuildQBXMLRequest(requestXml, _effectiveSDKVersion);
                var response = _connection.ProcessRequestWithRetry(fullRequest);
                var doc = System.Xml.Linq.XDocument.Parse(response);

                var existingTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var accountRet in doc.Descendants("AccountRet"))
                {
                    var accountType = accountRet.Element("AccountType")?.Value;
                    if (!string.IsNullOrEmpty(accountType))
                    {
                        existingTypes.Add(accountType);

                        // Also store the ListID mapping for existing accounts
                        var fullName = accountRet.Element("FullName")?.Value;
                        var listId = accountRet.Element("ListID")?.Value;
                        if (!string.IsNullOrEmpty(fullName) && !string.IsNullOrEmpty(listId))
                        {
                            _nameToListIdMap[$"Accounts:{fullName}"] = listId;
                        }
                    }
                }

                Log.Information("  FIX #3: Account types found in QB 2021: [{Types}]",
                    string.Join(", ", existingTypes.OrderBy(t => t)));

                // Check for missing types
                var missingTypes = requiredAccountTypes
                    .Where(t => !existingTypes.Contains(t))
                    .ToList();

                var missingCritical = missingTypes
                    .Where(t => criticalTypes.Contains(t))
                    .ToList();

                if (missingTypes.Any())
                {
                    Log.Warning("  FIX #3: Missing account types in QB 2021: [{Types}]",
                        string.Join(", ", missingTypes));
                }
                else
                {
                    Log.Information("  FIX #3: ✓ All {Count} required account types are available in QB 2021",
                        requiredAccountTypes.Length);
                }

                if (missingCritical.Any())
                {
                    Log.Error("  FIX #3: ⚠ CRITICAL account types missing: [{Types}]. " +
                        "Some imports may fail. Create these account types manually in QB 2021.",
                        string.Join(", ", missingCritical));
                }

                Log.Information("  FIX #3: Account type verification complete. " +
                    "{Found}/{Required} types available.",
                    existingTypes.Count, requiredAccountTypes.Length);
            }
            catch (Exception ex)
            {
                Log.Warning("  FIX #3: Could not verify account types (non-fatal): {Message}. " +
                    "Import will proceed and handle errors per-entity.", ex.Message);
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // FIX #4 / FIX #10: Create Missing Reference Types
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// FIX #10: Schema-driven configuration table for the pre-creation loop.
        ///
        /// Each entry describes ONE reference type that we know how to
        /// pre-create in QB 2021 before the main import stages run. Adding a new
        /// pre-createable ref type now requires only:
        ///   1. Adding a Referenced{X} HashSet on DependencyAnalysisResult and
        ///      routing it from AddReferencedItem (DependencyAnalyzer.cs).
        ///   2. Adding a new builder branch in AutoCreateSingleItem (if needed —
        ///      simple list types can use BuildSimpleListAddRequest directly).
        ///   3. Appending one row to this table.
        /// No new copy/paste of an "if (...Any()) { existing = ...; missing = ...; foreach {...} }"
        /// block per type.
        ///
        /// Tuple semantics:
        ///   EntityType        — token passed to QueryExistingItems / AutoCreateSingleItem
        ///   GetReferenced     — selector that returns the analyzer's referenced-name set
        ///   LogLabel          — singular label used in the per-name "Created X: 'Name'" log
        ///   FixTag            — comment tag emitted in the bulk log line ("FIX #4", "FIX #9", "FIX #10")
        ///   ExtraSuffix       — optional trailing note appended to the per-name log
        ///                       (e.g. " (default Net 30)") for transparency about placeholder values
        ///
        /// NOTE: Templates are NOT in this table — they cannot be created via QBXML SDK
        /// and are handled by a dedicated warning block below.
        /// </summary>
        private static readonly (
            string EntityType,
            Func<DependencyAnalysisResult, HashSet<string>> GetReferenced,
            string LogLabel,
            string FixTag,
            string ExtraSuffix)[] PreCreationConfig = new[]
        {
            ("SalesTaxCodes",  (Func<DependencyAnalysisResult, HashSet<string>>)(d => d.ReferencedSalesTaxCodes),  "SalesTaxCode",  "FIX #4",  ""),
            ("ItemSalesTax",   (Func<DependencyAnalysisResult, HashSet<string>>)(d => d.ReferencedSalesTaxItems),  "ItemSalesTax",  "FIX #9",  " (default TaxRate 0)"),
            ("Terms",          (Func<DependencyAnalysisResult, HashSet<string>>)(d => d.ReferencedTerms),          "Terms",         "FIX #4",  " (default Net 30)"),
            ("CustomerTypes",  (Func<DependencyAnalysisResult, HashSet<string>>)(d => d.ReferencedCustomerTypes),  "CustomerType",  "FIX #4",  ""),
            ("VendorTypes",    (Func<DependencyAnalysisResult, HashSet<string>>)(d => d.ReferencedVendorTypes),    "VendorType",    "FIX #4",  ""),
            ("JobTypes",       (Func<DependencyAnalysisResult, HashSet<string>>)(d => d.ReferencedJobTypes),       "JobType",       "FIX #4",  ""),
            ("PaymentMethods", (Func<DependencyAnalysisResult, HashSet<string>>)(d => d.ReferencedPaymentMethods), "PaymentMethod", "FIX #4",  ""),
            ("ShipMethods",    (Func<DependencyAnalysisResult, HashSet<string>>)(d => d.ReferencedShipMethods),    "ShipMethod",    "FIX #4",  ""),
            // FIX #10: PriceLevels referenced by customers (PriceLevelRef) — placeholder
            //          uses PriceLevelFixedPercentage of 0 (no actual discount applied).
            //          The real PriceLevel will be imported during Stage 1 if present in
            //          the exported data; this pre-creation only guarantees the ListID is
            //          resolvable when customers referencing it land in QB first.
            ("PriceLevels",    (Func<DependencyAnalysisResult, HashSet<string>>)(d => d.ReferencedPriceLevels),    "PriceLevel",    "FIX #10", " (placeholder 0%)"),
            // FIX #10: CustomerMsgs (e.g. "Thank you for your business!") referenced by
            //          Invoice/SalesReceipt/CreditMemo CustomerMsgRef. Created as a
            //          simple list item via BuildSimpleListAddRequest. Without this, any
            //          transaction carrying a CustomerMsgRef fails with QB Error 3140.
            ("CustomerMsgs",   (Func<DependencyAnalysisResult, HashSet<string>>)(d => d.ReferencedCustomerMsgs),   "CustomerMsg",   "FIX #10", ""),
        };

        /// <summary>
        /// FIX #4 + FIX #10: Creates missing reference types (sales tax codes, payment
        /// terms, customer types, ship methods, price levels, customer messages, …)
        /// BEFORE the main import stages so transactions/customers don't fail with
        /// StatusCode 3140 / 3200 when they reference items that don't exist yet.
        ///
        /// FIX #10 — Systematic schema-driven pre-creation:
        ///   Previously this method contained ~9 near-identical copy/pasted blocks,
        ///   one per ref type. Each block manually did:
        ///       1. Bail if the dependency set is empty.
        ///       2. Query QB for what already exists.
        ///       3. Diff referenced vs existing to produce a "missing" list.
        ///       4. Foreach missing → call AutoCreateSingleItem.
        ///       5. Tally totalCreated and accumulate the per-name log lines.
        ///   Adding a new ref type meant duplicating that block, fixing five names
        ///   inside, and remembering to bump totalCreated correctly. Easy to forget
        ///   a step (e.g. CustomerMsgs and PriceLevels were missing entirely before
        ///   this fix — that's why CustomerMsgRef references on transactions failed).
        ///
        ///   The new structure replaces all of those blocks with a single loop that
        ///   walks PreCreationConfig (a small static table mapping entity type →
        ///   analyzer selector → log labels). Adding a new pre-createable ref type
        ///   is now a one-line append to PreCreationConfig + a builder branch in
        ///   AutoCreateSingleItem.
        ///
        ///   Templates remain a special case: they cannot be created via QBXML SDK
        ///   (they're built into QB itself), so referencing them is handled below
        ///   with a dedicated warning block instead of being routed through the
        ///   pre-creation loop.
        /// </summary>
        private void CreateMissingReferenceTypes(DependencyAnalysisResult dependencies, StagedImportSummary summary)
        {
            var autoCreateSummary = new StageSummary
            {
                StageNumber = 0,
                StageName = "Create Missing Reference Types (FIX #4 / FIX #10)",
                StartTime = DateTime.UtcNow
            };

            int totalCreated = 0;

            // FIX #10: Single data-driven loop replaces the per-type copy/paste blocks.
            //          Order matters only for readability — each entry is independent.
            foreach (var entry in PreCreationConfig)
            {
                var referenced = entry.GetReferenced(dependencies);
                if (!referenced.Any()) continue;

                var existing = QueryExistingItems(entry.EntityType);
                var missing = referenced.Where(n => !existing.Contains(n)).ToList();
                if (!missing.Any()) continue;

                Log.Information("  {Fix}: Creating {Count} missing {Type}: [{Names}]",
                    entry.FixTag, missing.Count, entry.EntityType, string.Join(", ", missing));

                foreach (var name in missing)
                {
                    if (AutoCreateSingleItem(entry.EntityType, name))
                    {
                        totalCreated++;
                        autoCreateSummary.AutoCreatedItemNames.Add($"{entry.LogLabel}:{name}");
                        Log.Information("    ✓ Created {Label}: '{Name}'{Suffix}",
                            entry.LogLabel, name, entry.ExtraSuffix);
                    }
                }
            }

            // FIX #10: Templates handled separately — cannot be created via QBXML SDK.
            //          The destination QB company must already have a matching template
            //          installed (typically copied from the source QB's template list).
            //          We emit a single warning naming all referenced templates so the
            //          user can verify their presence before re-running the import.
            if (dependencies.ReferencedTemplates.Any())
            {
                Log.Warning("  FIX #10: {Count} Templates referenced but cannot be auto-created via QBXML SDK: [{Names}]",
                    dependencies.ReferencedTemplates.Count,
                    string.Join(", ", dependencies.ReferencedTemplates));
                Log.Warning("           Templates are built into QuickBooks. Ensure the destination QB company");
                Log.Warning("           has matching template names installed, or transactions referencing them");
                Log.Warning("           may fall back to the default template on import.");
            }

            autoCreateSummary.AutoCreatedItems = totalCreated;
            autoCreateSummary.EndTime = DateTime.UtcNow;
            autoCreateSummary.Passed = true;

            if (totalCreated > 0)
            {
                summary.Stages.Add(autoCreateSummary);
                Log.Information("  FIX #4 / FIX #10: Created {Count} missing reference types before import", totalCreated);
            }
            else
            {
                Log.Information("  FIX #4 / FIX #10: No missing reference types to create — all dependencies satisfied");
            }
        }

        /// <summary>
        /// Auto-creates missing foundation items that are referenced but don't exist in exported data.
        /// Creates them directly in QB 2021 before the main import begins.
        /// </summary>
        private void AutoCreateMissingItems(DependencyAnalysisResult dependencies, StagedImportSummary summary)
        {
            // We aggregate auto-creation into a virtual "Stage 0" summary for tracking
            var autoCreateSummary = new StageSummary
            {
                StageNumber = 0,
                StageName = "Auto-Create Missing Dependencies",
                StartTime = DateTime.UtcNow
            };

            foreach (var (entityType, missingNames) in dependencies.MissingItems)
            {
                // Only auto-create foundation items (Stage 1 types)
                if (!IsFoundationType(entityType)) continue;

                Log.Information("    Auto-creating {Count} missing {Type} items...",
                    missingNames.Count, entityType);

                foreach (var name in missingNames)
                {
                    bool created = AutoCreateSingleItem(entityType, name);
                    if (created)
                    {
                        autoCreateSummary.AutoCreatedItems++;
                        autoCreateSummary.AutoCreatedItemNames.Add($"{entityType}:{name}");
                        Log.Information("      ✓ Auto-created {Type}: '{Name}'", entityType, name);
                    }
                    else
                    {
                        Log.Warning("      ✗ Failed to auto-create {Type}: '{Name}'", entityType, name);
                    }
                }
            }

            autoCreateSummary.EndTime = DateTime.UtcNow;
            autoCreateSummary.Passed = true;

            if (autoCreateSummary.AutoCreatedItems > 0)
            {
                summary.Stages.Add(autoCreateSummary);
                Log.Information("    Auto-creation complete: {Count} items created",
                    autoCreateSummary.AutoCreatedItems);
            }
        }

        /// <summary>
        /// Checks if an entity type is a foundation type (Stage 1).
        /// </summary>
        private static bool IsFoundationType(string entityType)
        {
            return ImportStages[0].EntityTypes.Contains(entityType, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Creates a single missing item in QB 2021.
        /// Builds a minimal Add request for the item type.
        /// </summary>
        private bool AutoCreateSingleItem(string entityType, string name)
        {
            if (_importConfig.DryRun)
            {
                Log.Debug("      [DRY RUN] Would auto-create {Type}: '{Name}'", entityType, name);
                return true;
            }

            try
            {
                string requestXml = entityType switch
                {
                    "SalesTaxCodes" => BuildSalesTaxCodeAddRequest(name),
                    // FIX #9: route ItemSalesTax auto-create through a
                    // dedicated builder that emits a valid ItemSalesTaxAdd
                    // (Name + IsActive + TaxRate=0). TaxVendorRef is omitted
                    // intentionally because no vendor target is guaranteed
                    // to exist yet — QB 2021 will accept the placeholder.
                    "ItemSalesTax" => BuildItemSalesTaxAddRequest(name),
                    "Terms" => BuildTermsAddRequest(name),
                    "PaymentMethods" => BuildPaymentMethodAddRequest(name),
                    "CustomerTypes" => BuildSimpleListAddRequest("CustomerTypeAddRq", "CustomerTypeAdd", name),
                    "VendorTypes" => BuildSimpleListAddRequest("VendorTypeAddRq", "VendorTypeAdd", name),
                    "JobTypes" => BuildSimpleListAddRequest("JobTypeAddRq", "JobTypeAdd", name),
                    "ShipMethods" => BuildSimpleListAddRequest("ShipMethodAddRq", "ShipMethodAdd", name),
                    // FIX #10: PriceLevels need their own builder because the XSD requires
                    //          PriceLevelFixedPercentage (or PriceLevelPerItem). Simple Name+IsActive
                    //          is NOT accepted by QB 2021 — it returns Status 3170 ("Invalid Object").
                    "PriceLevels" => BuildPriceLevelAddRequest(name),
                    // FIX #10: CustomerMsgs are a plain list (Name + IsActive) — the simple builder works.
                    "CustomerMsgs" => BuildSimpleListAddRequest("CustomerMsgAddRq", "CustomerMsgAdd", name),
                    "Classes" => BuildClassAddRequestXml(name),
                    _ => string.Empty
                };

                if (string.IsNullOrEmpty(requestXml))
                {
                    Log.Warning("      No auto-create template for {Type}", entityType);
                    return false;
                }

                var fullRequest = QBConnectionManager.BuildQBXMLRequest(requestXml, _effectiveSDKVersion);
                var response = _connection.ProcessRequestWithRetry(fullRequest);
                var doc = XDocument.Parse(response);

                // Check for success or "already exists" (3100)
                var rsElement = doc.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName.EndsWith("Rs"));
                var statusCode = rsElement?.Attribute("statusCode")?.Value;

                if (statusCode == "0" || statusCode == "3100")
                {
                    // Store mapping if we got a ListID
                    var retElement = rsElement?.Elements().FirstOrDefault();
                    var listId = retElement?.Element("ListID")?.Value;
                    if (!string.IsNullOrEmpty(listId))
                    {
                        _nameToListIdMap[$"{entityType}:{name}"] = listId;
                    }
                    return true;
                }

                var statusMessage = rsElement?.Attribute("statusMessage")?.Value;
                Log.Warning("      Auto-create failed for {Type} '{Name}': [{Code}] {Message}",
                    entityType, name, statusCode, statusMessage);
                return false;
            }
            catch (Exception ex)
            {
                Log.Warning("      Error auto-creating {Type} '{Name}': {Message}",
                    entityType, name, ex.Message);
                return false;
            }
        }

        // ── Auto-create request builders ─────────────────────────────

        private string BuildSalesTaxCodeAddRequest(string name)
        {
            // SalesTaxCodeAdd requires Name and optionally IsTaxable
            // Default to taxable since it's being referenced as a tax code
            return $@"<SalesTaxCodeAddRq>
  <SalesTaxCodeAdd>
    <Name>{EscapeXml(name)}</Name>
    <IsTaxable>true</IsTaxable>
  </SalesTaxCodeAdd>
</SalesTaxCodeAddRq>";
        }

        /// <summary>
        /// FIX #9: Builds a minimal ItemSalesTaxAdd request.
        ///
        /// Per QBXML XSD (ItemSalesTaxAddOrder in QBXMLFieldOrdering.cs):
        ///   Name        — required (0)
        ///   BarCode     — optional (1)
        ///   IsActive    — optional (2)
        ///   ClassRef    — optional (3)
        ///   ItemDesc    — optional (4)
        ///   TaxRate     — required (5)         ← QB rejects ItemSalesTaxAdd without it
        ///   TaxVendorRef         — optional (6) but the referenced vendor must
        ///                          already exist; omitted to keep the request
        ///                          safe when the vendor is unknown.
        ///   SalesTaxReturnLineRef — optional (7) and equally vendor-dependent.
        ///
        /// We default TaxRate to 0 (a tax-exempt placeholder). The real tax
        /// rate will be set when the full ItemSalesTax entity is imported
        /// during Stage 1 via the normal Add path — auto-create here only
        /// needs to make the ListID resolvable for downstream references.
        /// </summary>
        private string BuildItemSalesTaxAddRequest(string name)
        {
            return $@"<ItemSalesTaxAddRq>
  <ItemSalesTaxAdd>
    <Name>{EscapeXml(name)}</Name>
    <IsActive>true</IsActive>
    <TaxRate>0</TaxRate>
  </ItemSalesTaxAdd>
</ItemSalesTaxAddRq>";
        }

        private string BuildTermsAddRequest(string name)
        {
            // StandardTermsAdd — default to Net 30
            return $@"<StandardTermsAddRq>
  <StandardTermsAdd>
    <Name>{EscapeXml(name)}</Name>
    <IsActive>true</IsActive>
    <StdDueDays>30</StdDueDays>
  </StandardTermsAdd>
</StandardTermsAddRq>";
        }

        private string BuildPaymentMethodAddRequest(string name)
        {
            return $@"<PaymentMethodAddRq>
  <PaymentMethodAdd>
    <Name>{EscapeXml(name)}</Name>
    <IsActive>true</IsActive>
  </PaymentMethodAdd>
</PaymentMethodAddRq>";
        }

        private string BuildSimpleListAddRequest(string reqType, string addType, string name)
        {
            return $@"<{reqType}>
  <{addType}>
    <Name>{EscapeXml(name)}</Name>
    <IsActive>true</IsActive>
  </{addType}>
</{reqType}>";
        }

        /// <summary>
        /// FIX #10: Builds a minimal PriceLevelAdd request.
        ///
        /// Per QBXML XSD, PriceLevelAdd requires:
        ///   Name        — required
        ///   IsActive    — optional
        ///   One of:
        ///     PriceLevelFixedPercentage (single decimal) — flat percentage discount/markup
        ///     PriceLevelPerItem (one or more nested PriceLevelPerItem entries) — per-item override
        /// QB 2021 will REJECT a PriceLevelAdd that has neither (StatusCode 3170 — "Invalid Object").
        ///
        /// For the auto-create placeholder we use PriceLevelFixedPercentage of 0 (no price change).
        /// If the source export also contains the PriceLevel as a Stage 1 entity, the
        /// real percentage / per-item rules will be applied when that entity is imported
        /// (QB will update the existing record by ListID/Name). The placeholder only needs
        /// to make the ListID resolvable so customers referencing PriceLevelRef can be
        /// imported in the correct stage order.
        /// </summary>
        private string BuildPriceLevelAddRequest(string name)
        {
            return $@"<PriceLevelAddRq>
  <PriceLevelAdd>
    <Name>{EscapeXml(name)}</Name>
    <IsActive>true</IsActive>
    <PriceLevelFixedPercentage>0</PriceLevelFixedPercentage>
  </PriceLevelAdd>
</PriceLevelAddRq>";
        }

        private string BuildClassAddRequestXml(string className)
        {
            var parts = className.Split(':');
            var leafName = parts.Last();
            var parentRef = parts.Length > 1
                ? $"\n    <ParentRef><FullName>{EscapeXml(string.Join(":", parts.Take(parts.Length - 1)))}</FullName></ParentRef>"
                : "";

            return $@"<ClassAddRq>
  <ClassAdd>
    <Name>{EscapeXml(leafName)}</Name>{parentRef}
    <IsActive>true</IsActive>
  </ClassAdd>
</ClassAddRq>";
        }

        /// <summary>
        /// Queries QB 2021 to check what items already exist for a given entity type.
        /// Returns a set of existing FullNames/Names.
        /// </summary>
        public HashSet<string> QueryExistingItems(string entityType)
        {
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string queryReq = entityType switch
            {
                "Accounts" => "AccountQueryRq",
                "SalesTaxCodes" => "SalesTaxCodeQueryRq",
                // FIX #9: dedicated ItemSalesTax query (generic ItemQuery
                // does NOT return ItemSalesTaxRet records).
                "ItemSalesTax" => "ItemSalesTaxQueryRq",
                "Terms" => "StandardTermsQueryRq",
                "PaymentMethods" => "PaymentMethodQueryRq",
                "Classes" => "ClassQueryRq",
                "CustomerTypes" => "CustomerTypeQueryRq",
                "VendorTypes" => "VendorTypeQueryRq",
                "JobTypes" => "JobTypeQueryRq",
                "ShipMethods" => "ShipMethodQueryRq",
                "PriceLevels" => "PriceLevelQueryRq",
                // FIX #10: CustomerMsgs query — required so pre-creation can diff
                //          referenced names against what's already present in QB 2021
                //          and skip recreation of any matching CustomerMsg.
                "CustomerMsgs" => "CustomerMsgQueryRq",
                "Customers" => "CustomerQueryRq",
                "Vendors" => "VendorQueryRq",
                _ => string.Empty
            };

            if (string.IsNullOrEmpty(queryReq)) return existing;

            string retType = entityType switch
            {
                "Accounts" => "AccountRet",
                "SalesTaxCodes" => "SalesTaxCodeRet",
                "ItemSalesTax" => "ItemSalesTaxRet", // FIX #9
                "Terms" => "StandardTermsRet",
                "PaymentMethods" => "PaymentMethodRet",
                "Classes" => "ClassRet",
                "CustomerTypes" => "CustomerTypeRet",
                "VendorTypes" => "VendorTypeRet",
                "JobTypes" => "JobTypeRet",
                "ShipMethods" => "ShipMethodRet",
                "PriceLevels" => "PriceLevelRet",
                "CustomerMsgs" => "CustomerMsgRet", // FIX #10
                "Customers" => "CustomerRet",
                "Vendors" => "VendorRet",
                _ => string.Empty
            };

            try
            {
                var requestXml = $"<{queryReq}><ActiveStatus>All</ActiveStatus></{queryReq}>";
                var fullRequest = QBConnectionManager.BuildQBXMLRequest(requestXml, _effectiveSDKVersion);
                var response = _connection.ProcessRequestWithRetry(fullRequest);
                var doc = XDocument.Parse(response);

                foreach (var ret in doc.Descendants(retType))
                {
                    var fullName = ret.Element("FullName")?.Value ?? ret.Element("Name")?.Value;
                    if (!string.IsNullOrEmpty(fullName))
                    {
                        existing.Add(fullName);

                        // Also store ListID mapping
                        var listId = ret.Element("ListID")?.Value;
                        if (!string.IsNullOrEmpty(listId))
                        {
                            _nameToListIdMap[$"{entityType}:{fullName}"] = listId;
                        }
                    }
                }

                Log.Debug("    Queried {Type}: {Count} existing items found", entityType, existing.Count);
            }
            catch (Exception ex)
            {
                Log.Warning("    Could not query existing {Type}: {Message}", entityType, ex.Message);
            }

            return existing;
        }

        // ═══════════════════════════════════════════════════════════════════
        // ORIGINAL ENTITY IMPORT (used by both legacy and staged paths)
        // ═══════════════════════════════════════════════════════════════════

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
            // For transactions, use TxnID or RefNumber as identifier; for list items, use Name/FullName
            bool isTransaction = TransactionEntityTypes.Contains(entityType);
            string sourceId;
            if (isTransaction)
            {
                // Transactions use TxnID, RefNumber, or TxnNumber — not Name/FullName
                sourceId = entity.TxnID;
                if (string.IsNullOrWhiteSpace(sourceId))
                    sourceId = entity.Fields["RefNumber"]?.ToString()
                            ?? entity.Fields["TxnNumber"]?.ToString()
                            ?? $"TxnIndex-{Guid.NewGuid():N}";
            }
            else
            {
                sourceId = !string.IsNullOrWhiteSpace(entity.FullName) ? entity.FullName
                         : !string.IsNullOrWhiteSpace(entity.Name) ? entity.Name
                         : entity.ListID;
            }

            var result = new ImportResult
            {
                EntityType = entityType,
                SourceIdentifier = sourceId
            };

            // Pre-validation: skip LIST entities with empty names (they'll fail anyway)
            // Transactions don't need Name/FullName — they use TxnID
            if (!isTransaction && string.IsNullOrWhiteSpace(entity.Name) && string.IsNullOrWhiteSpace(entity.FullName))
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
                // Look for the specific Add response element (e.g., AccountAddRs, CustomerAddRs)
                // NOT QBXMLMsgsRs which is the wrapper element
                var doc = XDocument.Parse(response);
                var rsElements = doc.Descendants()
                    .Where(e => e.Name.LocalName.EndsWith("Rs") && 
                                e.Name.LocalName != "QBXMLMsgsRs" &&
                                e.Attribute("statusCode") != null)
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
                    else if (IsAlreadyExistsError(statusCode, statusMessage))
                    {
                        // Entity already exists in target — treat as soft success
                        // Look up the existing entity's ListID for reference resolution
                        result.Success = true;
                        result.ErrorCode = statusCode;
                        result.ErrorMessage = $"[ALREADY EXISTS] {statusMessage}";
                        _alreadyExistsCount++;

                        Log.Information("  ✓ '{Name}' already exists in target — treating as success (Error {Code})",
                            sourceId, statusCode);

                        // Try to look up the existing entity's ListID
                        TryLookupExistingEntity(entityType, entity, result);
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
                // BONUS FIX: Don't overwrite success status if the entity was already
                // successfully imported. This prevents retry logic from marking a
                // successful import as failed when a non-critical exception occurs
                // after the QB response was already processed.
                if (!result.Success)
                {
                    result.Success = false;
                    result.ErrorMessage = ex.Message;
                }
                else
                {
                    Log.Warning("  Exception after successful import of '{Name}' (preserving success): {Message}",
                        entity.Name, ex.Message);
                }
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
        /// Checks if a QBXML error indicates the entity already exists in the target file.
        /// Error 3100: "The name 'X' of the list element is already in use."
        /// Error 3180: "There was an error when saving a X list, element 'Y'. QuickBooks error message: The account number is already in use."
        /// </summary>
        private static bool IsAlreadyExistsError(string? statusCode, string? statusMessage)
        {
            if (string.IsNullOrEmpty(statusCode) || string.IsNullOrEmpty(statusMessage))
                return false;

            var msg = statusMessage.ToUpperInvariant();

            // Error 3100: name already in use
            if (statusCode == "3100" && msg.Contains("ALREADY IN USE"))
                return true;

            // Error 3180: account number already in use, or other save errors indicating duplicates
            if (statusCode == "3180" && (msg.Contains("ALREADY IN USE") || msg.Contains("ALREADY EXISTS")))
                return true;

            // Error 3110: name already in use (alternate code)
            if (statusCode == "3110" && msg.Contains("ALREADY IN USE"))
                return true;

            return false;
        }

        /// <summary>
        /// When an entity already exists in the target, look up its ListID so we can
        /// store the ID mapping for reference resolution in later stages.
        /// </summary>
        private void TryLookupExistingEntity(string entityType, QBEntity entity, ImportResult result)
        {
            try
            {
                // Determine the query type from the entity type
                var queryMap = new Dictionary<string, (string QueryType, string RetType)>
                {
                    ["Accounts"] = ("AccountQuery", "AccountRet"),
                    ["Customers"] = ("CustomerQuery", "CustomerRet"),
                    ["Vendors"] = ("VendorQuery", "VendorRet"),
                    ["Employees"] = ("EmployeeQuery", "EmployeeRet"),
                    ["Items"] = ("ItemQuery", "ItemRet"),
                    ["Classes"] = ("ClassQuery", "ClassRet"),
                    ["SalesTaxCodes"] = ("SalesTaxCodeQuery", "SalesTaxCodeRet"),
                    // FIX #9: dedicated lookup so "already exists" recovery
                    // can fetch the ListID of a pre-existing ItemSalesTax in
                    // QB 2021 and store it for later reference resolution.
                    ["ItemSalesTax"] = ("ItemSalesTaxQuery", "ItemSalesTaxRet"),
                    ["Terms"] = ("TermsQuery", "StandardTermsRet"),
                    ["PaymentMethods"] = ("PaymentMethodQuery", "PaymentMethodRet"),
                    ["CustomerTypes"] = ("CustomerTypeQuery", "CustomerTypeRet"),
                    ["VendorTypes"] = ("VendorTypeQuery", "VendorTypeRet"),
                    ["JobTypes"] = ("JobTypeQuery", "JobTypeRet"),
                    ["ShipMethods"] = ("ShipMethodQuery", "ShipMethodRet"),
                    ["PriceLevels"] = ("PriceLevelQuery", "PriceLevelRet"),
                    ["CustomerMsgs"] = ("CustomerMsgQuery", "CustomerMsgRet"),
                };

                if (!queryMap.ContainsKey(entityType))
                {
                    Log.Debug("  No query mapping for {EntityType} — cannot look up existing entity", entityType);
                    return;
                }

                var (queryType, retType) = queryMap[entityType];
                var nameToFind = entity.FullName ?? entity.Name;
                if (string.IsNullOrEmpty(nameToFind)) return;

                // Build a query to find the entity by name
                var queryXml = $"<{queryType}Rq><FullName>{EscapeXml(nameToFind)}</FullName></{queryType}Rq>";
                var fullRequest = QBConnectionManager.BuildQBXMLRequest(queryXml, _effectiveSDKVersion);

                var response = _connection.ProcessRequest(fullRequest);
                var entities = QBConnectionManager.ParseResponseEntities(response, retType);

                if (entities.Any())
                {
                    var existingListID = entities.First().Element("ListID")?.Value;
                    if (!string.IsNullOrEmpty(existingListID))
                    {
                        result.NewListID = existingListID;
                        Log.Debug("  Found existing ListID for '{Name}': {ListID}", nameToFind, existingListID);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("  Could not look up existing entity '{Name}': {Error}",
                    entity.Name, ex.Message);
            }
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
                    // Check for empty names — only for list entities, not transactions
                    bool isTxn = TransactionEntityTypes.Contains(entityType);
                    if (!isTxn && string.IsNullOrWhiteSpace(entity.Name) && string.IsNullOrWhiteSpace(entity.FullName))
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
        /// Fields are sorted according to QBXML XSD schema sequence requirements.
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

            // Use entity type for field ordering — QBXML requires strict XSD element sequence
            var fieldsXml = BuildFieldsXml(entity.Fields, entityType);

            // ═══════════════════════════════════════════════════════════════════
            // FIX #15: Diagnostic — log LineItems count before QBXML generation.
            // This confirms whether line items survived deserialization and
            // transformation. If count is 0 for a transaction type that requires
            // line items, the QBXML will be rejected by QB 2021.
            // ═══════════════════════════════════════════════════════════════════
            if (entity.LineItems == null)
            {
                Log.Warning("  FIX #15: entity.LineItems is NULL for {EntityType} '{Name}' — initializing empty list",
                    entityType, entity.Name ?? entity.TxnID ?? "unknown");
                entity.LineItems = new List<JObject>();
            }
            Log.Debug("  FIX #15: {EntityType} '{Name}' has {Count} line items before QBXML generation",
                entityType, entity.Name ?? entity.TxnID ?? "unknown", entity.LineItems.Count);

            var lineItemsXml = BuildLineItemsXml(entityType, entity.LineItems);

            // FIX #11: Diagnostic — warn if a transaction type that REQUIRES line items has none
            if (string.IsNullOrEmpty(lineItemsXml) && entity.LineItems.Count == 0)
            {
                var lineRequiringTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "Checks", "Deposits", "JournalEntries", "SalesReceipts",
                    "Invoices", "Bills", "CreditMemos", "Estimates", "PurchaseOrders", "VendorCredits"
                };
                if (lineRequiringTypes.Contains(entityType))
                {
                    Log.Warning("  FIX #11: {EntityType} entity '{Name}' has 0 line items — QBXML Add will likely fail. " +
                        "Verify that IncludeLineItems=true was set in the export query.",
                        entityType, entity.Name ?? entity.TxnID ?? "unknown");
                }
            }

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
        /// Fields are sorted according to the item-specific XSD sequence.
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

            // Use item subtype for field ordering (e.g., "ItemService", "ItemInventory")
            var fieldsXml = BuildFieldsXml(fieldsClone, itemType);

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
        /// 
        /// CRITICAL: Fields are sorted according to QBXML XSD schema sequence order.
        /// The QBXML parser uses xs:sequence, meaning elements MUST appear in the exact
        /// order defined by the schema. Out-of-order elements cause 0x80040400 errors:
        ///   "QuickBooks found an error when parsing the provided XML text stream."
        /// </summary>
        /// <param name="fields">The JObject containing entity field data.</param>
        /// <param name="entityType">Entity type name used to look up the correct field ordering.
        /// Accepts collection names (e.g., "Customers"), Add element names (e.g., "CustomerAdd"),
        /// or item subtypes (e.g., "ItemService").</param>
        private string BuildFieldsXml(JObject fields, string entityType)
        {
            var sb = new StringBuilder();

            // ── FIX #1: Hierarchical Name Handling ─────────────────────────
            // For entity types that support hierarchy (Accounts, Customers, Vendors, Items, Classes),
            // parse the FullName to extract the leaf Name and ParentRef.
            // QB requires <Name> to be the leaf only, with <ParentRef> for the parent.
            bool isHierarchical = HierarchicalEntityTypes.Contains(entityType);
            string? hierarchicalParent = null;
            bool nameFieldEmitted = false;

            if (isHierarchical)
            {
                // Determine the full name: prefer FullName field, fall back to Name
                var fullName = fields["FullName"]?.ToString() ?? fields["Name"]?.ToString();
                if (!string.IsNullOrEmpty(fullName) && fullName.Contains(':'))
                {
                    var (leafName, parentFullName) = ExtractLeafAndParent(fullName);
                    hierarchicalParent = parentFullName;

                    Log.Debug("  FIX #1: Hierarchical name for {EntityType}: full='{Full}', leaf='{Leaf}', parent='{Parent}'",
                        entityType, fullName, leafName, parentFullName);
                }
            }

            // Get the XSD-defined field ordering for this entity type
            var fieldOrderMap = QBXMLFieldOrdering.GetFieldOrderMap(entityType);

            // Sort properties by their XSD sequence index.
            // Fields not in the map get index 9999 (appended at the end).
            // Alphabetical tiebreaker for fields with the same index (or unmapped).
            var sortedProperties = fields.Properties()
                .OrderBy(p => QBXMLFieldOrdering.GetFieldIndex(fieldOrderMap, p.Name))
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (fieldOrderMap.Count > 0)
            {
                Log.Debug("  QBXML field ordering applied for '{EntityType}': [{FieldOrder}]",
                    entityType,
                    string.Join(", ", sortedProperties
                        .Where(p => !p.Name.StartsWith("_") && !ExcludedFields.Contains(p.Name))
                        .Select(p => p.Name)));
            }

            // ── FIX #2: Track whether we need to force IsActive ──────────
            bool isActiveEmitted = false;

            // ── FIX #8 / FIX #16: Look up per-entity-type header exclusions ──
            // For transactions AND list entities (e.g., Employees), certain
            // fields returned in the *Ret response (Name, header Amount,
            // computed totals, read-only balances) are NOT valid in the *Add
            // request. We strip them here so the resulting QBXML conforms to
            // the QB 2021 XSD schema. Without this, QuickBooks rejects the
            // entire request with 0x80040400.
            // FIX #16 extended this to Employees: the combined <Name> field
            // conflicts with split <FirstName>/<MiddleName>/<LastName>.
            HashSet<string>? txnHeaderExcl = null;
            TransactionHeaderExcludedFields.TryGetValue(entityType, out txnHeaderExcl);
            Log.Debug("  FIX #16 DEBUG: entityType='{EntityType}', txnHeaderExcl={HasExcl}, fields=[{Fields}]",
                entityType,
                txnHeaderExcl != null ? string.Join(",", txnHeaderExcl) : "NULL",
                string.Join(",", sortedProperties.Select(p => p.Name)));

            foreach (var prop in sortedProperties)
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

                // ── FIX #8: Per-transaction-type header-field exclusion ───
                // Applied at the top-level loop only — child elements inside
                // nested objects (e.g., <Address><Name>...</Name></Address>)
                // are intentionally NOT affected by this map.
                if (txnHeaderExcl != null && txnHeaderExcl.Contains(prop.Name))
                {
                    _incompatibleFieldSkips++;
                    Log.Debug("  FIX #8: Excluding '{Field}' from {EntityType} header " +
                        "(not in *Add schema or belongs on a line item)",
                        prop.Name, entityType);
                    continue;
                }

                // Skip SDK 16-only fields that may have slipped through transformation
                if (QBSDKVersionHelper.SDK16OnlyFields.Contains(prop.Name))
                {
                    _incompatibleFieldSkips++;
                    Log.Debug("  Skipping SDK 16-only field '{Field}' in QBXML generation", prop.Name);
                    continue;
                }

                // ── FIX #5: Additional field compatibility check ──────────
                // Skip DetailAccountType (not in SDK 15.0) and other 2023-only fields
                if (IsQB2023OnlyField(prop.Name))
                {
                    _incompatibleFieldSkips++;
                    Log.Debug("  Skipping QB 2023-only field '{Field}' in QBXML generation (FIX #5)", prop.Name);
                    continue;
                }

                if (prop.Name.StartsWith("_"))
                    continue; // Skip internal metadata fields

                // ── FIX #1: Handle Name field for hierarchical entities ───
                if (prop.Name == "Name" && isHierarchical)
                {
                    var nameValue = prop.Value.ToString();
                    if (!string.IsNullOrEmpty(nameValue))
                    {
                        // Extract leaf name (strip parent path if present)
                        var (leafName, _) = ExtractLeafAndParent(nameValue);
                        var safeLeafName = EnforceFieldLength("Name", leafName);
                        sb.AppendLine($"    <Name>{EscapeXml(safeLeafName)}</Name>");
                        nameFieldEmitted = true;
                    }
                    continue; // Don't process Name as normal field
                }

                // ── FIX #2: Force IsActive = true for all entities ────────
                // Accountant requirement: all entities must be active during import.
                // Inactive entities cause reference resolution failures.
                if (prop.Name == "IsActive")
                {
                    sb.AppendLine($"    <IsActive>true</IsActive>");
                    isActiveEmitted = true;
                    if (prop.Value.ToString().ToLowerInvariant() != "true")
                    {
                        Log.Debug("  FIX #2: IsActive overridden to 'true' for {EntityType} (was '{Original}')",
                            entityType, prop.Value.ToString());
                    }
                    continue; // Don't process IsActive as normal field
                }

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
                        // Regular nested object (like Address) — sort internal fields too
                        var childOrderMap = QBXMLFieldOrdering.IsAddressElement(prop.Name)
                            ? QBXMLFieldOrdering.GetAddressFieldOrder()
                            : new Dictionary<string, int>();

                        var sortedChildren = nested.Properties()
                            .OrderBy(c => QBXMLFieldOrdering.GetFieldIndex(childOrderMap, c.Name))
                            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        sb.AppendLine($"    <{prop.Name}>");
                        foreach (var childProp in sortedChildren)
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
                    
                    // ── FIX #7: Strip timezone offsets from date/time fields ──────────
                    // QBXML does NOT support timezone offsets (e.g., -06:00).
                    // All date fields must be in format: yyyy-MM-dd OR yyyy-MM-ddTHH:mm:ss
                    if (IsDateTimeField(prop.Name) && ContainsTimezoneOffset(value))
                    {
                        value = StripTimezoneOffset(value);
                        Log.Debug("  FIX #7: Stripped timezone offset from {Field}: {Value}", prop.Name, value);
                    }
                    
                    sb.AppendLine($"    <{prop.Name}>{EscapeXml(value)}</{prop.Name}>");
                }
            }

            // ── FIX #1: Emit ParentRef after Name if hierarchical ─────────
            if (isHierarchical && !string.IsNullOrEmpty(hierarchicalParent))
            {
                // If Name wasn't emitted yet (e.g., data only had FullName), emit it now
                if (!nameFieldEmitted)
                {
                    var fullName = fields["FullName"]?.ToString() ?? fields["Name"]?.ToString() ?? "";
                    var (leafName, _) = ExtractLeafAndParent(fullName);
                    var safeLeafName = EnforceFieldLength("Name", leafName);
                    // Prepend Name at the beginning of sb
                    sb.Insert(0, $"    <Name>{EscapeXml(safeLeafName)}</Name>\n");
                }

                sb.AppendLine($"    <ParentRef>");
                sb.AppendLine($"      <FullName>{EscapeXml(hierarchicalParent)}</FullName>");
                sb.AppendLine($"    </ParentRef>");
                Log.Information("  FIX #1: Added ParentRef '{Parent}' for hierarchical entity in {EntityType}",
                    hierarchicalParent, entityType);
            }

            // ── FIX #2: Force IsActive if not already emitted ─────────────
            // Ensure all entities that support IsActive get it set to true
            if (!isActiveEmitted && IsActiveEntityType(entityType))
            {
                sb.AppendLine($"    <IsActive>true</IsActive>");
                Log.Debug("  FIX #2: Forced IsActive=true for {EntityType} (field was missing)", entityType);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Entity types that support the IsActive field.
        /// FIX #2: All of these must have IsActive=true during import.
        /// </summary>
        private static bool IsActiveEntityType(string entityType)
        {
            return entityType switch
            {
                "Customers" or "CustomerAdd" => true,
                "Vendors" or "VendorAdd" => true,
                "Employees" or "EmployeeAdd" => true,
                "Accounts" or "AccountAdd" => true,
                "Items" or "ItemService" or "ItemInventory" or "ItemNonInventory" or
                "ItemOtherCharge" or "ItemDiscount" or "ItemGroup" or "ItemSalesTax" => true,
                "Classes" or "ClassAdd" => true,
                "SalesTaxCodes" or "SalesTaxCodeAdd" => true,
                "Terms" or "StandardTermsAdd" => true,
                "PaymentMethods" or "PaymentMethodAdd" => true,
                _ => false
            };
        }

        /// <summary>
        /// FIX #5: Fields that are QB 2023-specific and not supported in SDK 15.0 (QB 2021).
        /// These are beyond the SDK16OnlyFields list — additional fields found during testing.
        /// </summary>
        private static bool IsQB2023OnlyField(string fieldName)
        {
            return fieldName switch
            {
                // DetailAccountType not supported in SDK 15.0 — only AccountType is valid
                "DetailAccountType" => true,
                // Tax form mapping fields from QB 2023
                "TaxLineID" => true,
                // Enhanced inventory fields
                "QuantityOnSalesOrder" => true,
                "QuantityOnPurchaseOrder" => true,
                // Sub-item depth tracking (read-only field)
                "Sublevel" => true,
                // Special order fields
                "SpecialItemType" => true,
                _ => false
            };
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
        /// FIX #7: Determines if a field name represents a date/time field.
        /// </summary>
        private static bool IsDateTimeField(string fieldName)
        {
            return fieldName.Contains("Date", StringComparison.OrdinalIgnoreCase) ||
                   fieldName.Contains("Time", StringComparison.OrdinalIgnoreCase) ||
                   fieldName == "TxnDate" ||
                   fieldName == "DueDate" ||
                   fieldName == "ShipDate" ||
                   fieldName == "ServiceDate";
        }

        /// <summary>
        /// FIX #7: Checks if a date string contains a timezone offset (e.g., -06:00, +05:30).
        /// </summary>
        private static bool ContainsTimezoneOffset(string value)
        {
            // Match patterns like: -06:00, +05:30, Z
            return System.Text.RegularExpressions.Regex.IsMatch(value, @"[+-]\d{2}:\d{2}$") ||
                   value.EndsWith("Z", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// FIX #7: Strips timezone offset from a datetime string.
        /// Converts: 2026-05-10T23:29:48-06:00 → 2026-05-10T23:29:48
        /// Also handles: 2026-05-10T23:29:48Z → 2026-05-10T23:29:48
        /// </summary>
        private static string StripTimezoneOffset(string value)
        {
            // Remove trailing Z
            if (value.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
            {
                return value.Substring(0, value.Length - 1);
            }

            // Remove timezone offset pattern (e.g., -06:00, +05:30)
            var timezonePattern = @"[+-]\d{2}:\d{2}$";
            return System.Text.RegularExpressions.Regex.Replace(value, timezonePattern, "");
        }

        /// <summary>
        /// Builds QBXML for line items in transaction entities.
        /// Line item fields are sorted according to their XSD-defined sequence order.
        /// </summary>
        private string BuildLineItemsXml(string entityType, List<JObject> lineItems)
        {
            if (!lineItems.Any()) return string.Empty;

            var sb = new StringBuilder();
            var lineAddType = GetLineAddType(entityType);

            foreach (var lineItem in lineItems)
            {
                var lineType = lineItem["_lineType"]?.ToString() ?? lineAddType;

                // ========================================================================
                // FIX #8: JournalEntry line-element naming
                // ========================================================================
                // For most line-bearing transactions the QBXML *Add request uses
                // <FooLineAdd> (e.g. <ExpenseLineAdd>, <DepositLineAdd>, <SalesReceiptLineAdd>).
                //
                // BUT JournalEntryAdd is special — its child elements are literally
                //   <JournalDebitLine> ... </JournalDebitLine>
                //   <JournalCreditLine> ... </JournalCreditLine>
                // with NO "Add" suffix. The existing logic of
                //   lineType.Replace("Ret","Add")
                // would emit <JournalDebitLineAdd>, which QuickBooks rejects with
                //   "QuickBooks found an error when parsing the provided XML text stream"
                //   (0x80040400).
                //
                // Special-case these two element names so JournalEntryAdd posts cleanly.
                // GetLineItemFieldOrder("JournalDebitLine") still works because it
                // strips Add/Ret then matches Contains("JournalDebit").
                // ========================================================================
                string addLineType;
                if (lineType.Equals("JournalDebitLineRet", StringComparison.OrdinalIgnoreCase) ||
                    lineType.Equals("JournalDebitLine", StringComparison.OrdinalIgnoreCase))
                {
                    addLineType = "JournalDebitLine"; // no "Add" suffix per QBXML schema
                }
                else if (lineType.Equals("JournalCreditLineRet", StringComparison.OrdinalIgnoreCase) ||
                         lineType.Equals("JournalCreditLine", StringComparison.OrdinalIgnoreCase))
                {
                    addLineType = "JournalCreditLine"; // no "Add" suffix per QBXML schema
                }
                else
                {
                    // Convert Ret suffix to Add suffix for the request
                    addLineType = lineType.Replace("Ret", "Add").Replace("LineRet", "LineAdd");
                    if (!addLineType.EndsWith("Add") && !addLineType.Contains("LineAdd"))
                        addLineType = lineAddType;
                }

                // Get XSD field ordering for this line item type
                var lineFieldOrder = QBXMLFieldOrdering.GetLineItemFieldOrder(addLineType);

                // Sort line item properties by XSD sequence order
                var sortedLineProps = lineItem.Properties()
                    .OrderBy(p => QBXMLFieldOrdering.GetFieldIndex(lineFieldOrder, p.Name))
                    .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                sb.AppendLine($"    <{addLineType}>");

                foreach (var prop in sortedLineProps)
                {
                    if (prop.Name.StartsWith("_")) continue; // Skip metadata
                    if (ExcludedFields.Contains(prop.Name)) continue;

                    // ══════════════════════════════════════════════════════════
                    // FIX #12: Skip response-only fields in line items
                    // ══════════════════════════════════════════════════════════
                    // Line items from the export contain fields that are only
                    // valid in *Ret responses, not in *Add requests. Including
                    // them causes 0x80040400 XML parse errors.
                    if (LineItemExcludedFields.Contains(prop.Name))
                    {
                        _incompatibleFieldSkips++;
                        continue;
                    }

                    if (prop.Value is JObject nested)
                    {
                        // ══════════════════════════════════════════════════════
                        // FIX #12: For reference objects (AccountRef, ItemRef,
                        // ClassRef, etc.) in line items, use ONLY FullName.
                        // ListIDs from QB 2023 are invalid in QB 2021 and cause
                        // XML parse errors or Error 3120 "object not found".
                        // ══════════════════════════════════════════════════════
                        bool isRef = prop.Name.EndsWith("Ref");
                        if (isRef && nested["FullName"] != null)
                        {
                            sb.AppendLine($"      <{prop.Name}>");
                            sb.AppendLine($"        <FullName>{EscapeXml(nested["FullName"]!.ToString())}</FullName>");
                            sb.AppendLine($"      </{prop.Name}>");
                        }
                        else if (isRef && nested["ListID"] != null)
                        {
                            // Fallback: if only ListID exists (no FullName), skip the ref entirely
                            // because the QB 2023 ListID won't resolve in QB 2021
                            Log.Warning("  FIX #12: Skipping {RefField} in line item — has ListID but no FullName (QB 2023 ListID won't resolve in QB 2021)",
                                prop.Name);
                            _incompatibleFieldSkips++;
                        }
                        else
                        {
                            // Non-reference nested object — emit all children
                            sb.AppendLine($"      <{prop.Name}>");
                            foreach (var childProp in nested.Properties())
                            {
                                if (childProp.Value.Type != JTokenType.Null &&
                                    !string.IsNullOrEmpty(childProp.Value.ToString()))
                                {
                                    // FIX #7: Strip timezone offsets from nested date/time fields in line items
                                    var nestedValue = childProp.Value.ToString();
                                    if (IsDateTimeField(childProp.Name) && ContainsTimezoneOffset(nestedValue))
                                    {
                                        nestedValue = StripTimezoneOffset(nestedValue);
                                    }
                                    sb.AppendLine($"        <{childProp.Name}>{EscapeXml(nestedValue)}</{childProp.Name}>");
                                }
                            }
                            sb.AppendLine($"      </{prop.Name}>");
                        }
                    }
                    else if (prop.Value.Type != JTokenType.Null && !string.IsNullOrEmpty(prop.Value.ToString()))
                    {
                        // FIX #7: Strip timezone offsets from line item date/time fields too
                        var lineValue = prop.Value.ToString();
                        if (IsDateTimeField(prop.Name) && ContainsTimezoneOffset(lineValue))
                        {
                            lineValue = StripTimezoneOffset(lineValue);
                        }
                        sb.AppendLine($"      <{prop.Name}>{EscapeXml(lineValue)}</{prop.Name}>");
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
                "JournalEntries" => "JournalDebitLine", // FIX #8: no "Add" suffix per QBXML schema
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

        // ═══════════════════════════════════════════════════════════════════
        // HIERARCHICAL NAME HELPERS (FIX #1)
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Entity types that support hierarchical names with ParentRef in QBXML Add requests.
        /// These entities use colon-separated FullNames (e.g., "Parent:Child:GrandChild").
        /// When adding, we must split into leaf Name + ParentRef.
        /// </summary>
        private static readonly HashSet<string> HierarchicalEntityTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "Accounts", "Customers", "Vendors", "Items", "Classes",
            "ItemService", "ItemInventory", "ItemNonInventory", "ItemOtherCharge",
            "ItemDiscount", "ItemGroup", "ItemSalesTax"
        };

        /// <summary>
        /// Extracts the leaf name and parent full name from a hierarchical QB name.
        /// QuickBooks uses colon ':' as the hierarchy separator.
        /// Example: "Expenses:Office:Supplies" → ("Supplies", "Expenses:Office")
        /// </summary>
        /// <param name="fullName">The colon-separated full name</param>
        /// <returns>Tuple of (leafName, parentFullName). parentFullName is null if no parent.</returns>
        private static (string leafName, string? parentFullName) ExtractLeafAndParent(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
                return (fullName ?? string.Empty, null);

            var parts = fullName.Split(':');
            if (parts.Length <= 1)
                return (fullName.Trim(), null);

            var leafName = parts[parts.Length - 1].Trim();
            var parentFullName = string.Join(":", parts.Take(parts.Length - 1)).Trim();

            if (string.IsNullOrEmpty(parentFullName))
                return (leafName, null);

            Log.Debug("  Hierarchical name parsed: '{FullName}' → leaf='{Leaf}', parent='{Parent}'",
                fullName, leafName, parentFullName);

            return (leafName, parentFullName);
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
