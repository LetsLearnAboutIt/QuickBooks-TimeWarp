using System.Xml.Linq;
using Newtonsoft.Json;
using QB_TimeWarp.Models;
using Serilog;

namespace QB_TimeWarp.Services
{
    /// <summary>
    /// Extracts field definitions and schema information from a QuickBooks company file.
    /// Connects to QB 2021 (blank file) to document all supported fields, data types, and constraints.
    /// 
    /// The schema extraction works by:
    /// 1. Sending introspection queries to discover available request/response types
    /// 2. Parsing the QBXML SDK documentation embedded in the SDK's OSR (OnScreen Reference)
    /// 3. Querying each entity type to see what fields come back in the response
    /// </summary>
    public class SchemaExtractor
    {
        private readonly QBConnectionManager _connection;
        private readonly string _sdkVersion;
        private readonly string _outputDirectory;

        /// <summary>
        /// Well-known entity type definitions mapping entity names to their QBXML query/response types.
        /// This is the authoritative reference for all supported entity types.
        /// </summary>
        private static readonly Dictionary<string, (string QueryRq, string ResponseRet, string AddRq, string AddType, bool IsTransaction)> EntityDefinitions = new()
        {
            // List entities
            ["Account"]       = ("AccountQueryRq",       "AccountRet",       "AccountAddRq",       "AccountAdd",       false),
            ["Customer"]      = ("CustomerQueryRq",      "CustomerRet",      "CustomerAddRq",      "CustomerAdd",      false),
            ["Vendor"]        = ("VendorQueryRq",        "VendorRet",        "VendorAddRq",        "VendorAdd",        false),
            ["Employee"]      = ("EmployeeQueryRq",      "EmployeeRet",      "EmployeeAddRq",      "EmployeeAdd",      false),
            ["ItemService"]   = ("ItemServiceQueryRq",   "ItemServiceRet",   "ItemServiceAddRq",   "ItemServiceAdd",   false),
            ["ItemInventory"] = ("ItemInventoryQueryRq", "ItemInventoryRet", "ItemInventoryAddRq", "ItemInventoryAdd", false),
            ["ItemNonInventory"] = ("ItemNonInventoryQueryRq", "ItemNonInventoryRet", "ItemNonInventoryAddRq", "ItemNonInventoryAdd", false),
            ["ItemOtherCharge"] = ("ItemOtherChargeQueryRq", "ItemOtherChargeRet", "ItemOtherChargeAddRq", "ItemOtherChargeAdd", false),
            ["ItemDiscount"]  = ("ItemDiscountQueryRq",  "ItemDiscountRet",  "ItemDiscountAddRq",  "ItemDiscountAdd",  false),
            ["ItemGroup"]     = ("ItemGroupQueryRq",     "ItemGroupRet",     "ItemGroupAddRq",     "ItemGroupAdd",     false),
            ["ItemSalesTax"]  = ("ItemSalesTaxQueryRq",  "ItemSalesTaxRet",  "ItemSalesTaxAddRq",  "ItemSalesTaxAdd",  false),
            ["PaymentMethod"] = ("PaymentMethodQueryRq", "PaymentMethodRet", "PaymentMethodAddRq", "PaymentMethodAdd", false),
            ["Terms"]         = ("TermsQueryRq",         "StandardTermsRet", "StandardTermsAddRq", "StandardTermsAdd", false),
            ["Class"]         = ("ClassQueryRq",         "ClassRet",         "ClassAddRq",         "ClassAdd",         false),
            ["SalesTaxCode"]  = ("SalesTaxCodeQueryRq",  "SalesTaxCodeRet",  "SalesTaxCodeAddRq",  "SalesTaxCodeAdd",  false),
            ["ShipMethod"]    = ("ShipMethodQueryRq",    "ShipMethodRet",    "ShipMethodAddRq",    "ShipMethodAdd",    false),
            ["CustomerType"]  = ("CustomerTypeQueryRq",  "CustomerTypeRet",  "CustomerTypeAddRq",  "CustomerTypeAdd",  false),
            ["VendorType"]    = ("VendorTypeQueryRq",    "VendorTypeRet",    "VendorTypeAddRq",    "VendorTypeAdd",    false),
            ["JobType"]       = ("JobTypeQueryRq",       "JobTypeRet",       "JobTypeAddRq",       "JobTypeAdd",       false),
            ["PriceLevel"]    = ("PriceLevelQueryRq",    "PriceLevelRet",    "PriceLevelAddRq",    "PriceLevelAdd",    false),
            ["CustomerMsg"]   = ("CustomerMsgQueryRq",   "CustomerMsgRet",   "CustomerMsgAddRq",   "CustomerMsgAdd",   false),

            // Transaction entities
            ["Invoice"]       = ("InvoiceQueryRq",       "InvoiceRet",       "InvoiceAddRq",       "InvoiceAdd",       true),
            ["Bill"]          = ("BillQueryRq",          "BillRet",          "BillAddRq",          "BillAdd",          true),
            ["ReceivePayment"] = ("ReceivePaymentQueryRq", "ReceivePaymentRet", "ReceivePaymentAddRq", "ReceivePaymentAdd", true),
            ["SalesReceipt"]  = ("SalesReceiptQueryRq",  "SalesReceiptRet",  "SalesReceiptAddRq",  "SalesReceiptAdd",  true),
            ["PurchaseOrder"] = ("PurchaseOrderQueryRq", "PurchaseOrderRet", "PurchaseOrderAddRq", "PurchaseOrderAdd", true),
            ["JournalEntry"]  = ("JournalEntryQueryRq",  "JournalEntryRet",  "JournalEntryAddRq",  "JournalEntryAdd",  true),
            ["CreditMemo"]    = ("CreditMemoQueryRq",    "CreditMemoRet",    "CreditMemoAddRq",    "CreditMemoAdd",    true),
            ["Estimate"]      = ("EstimateQueryRq",      "EstimateRet",      "EstimateAddRq",      "EstimateAdd",      true),
            ["Deposit"]       = ("DepositQueryRq",       "DepositRet",       "DepositAddRq",       "DepositAdd",       true),
            ["Check"]         = ("CheckQueryRq",         "CheckRet",         "CheckAddRq",         "CheckAdd",         true),
            ["VendorCredit"]  = ("VendorCreditQueryRq",  "VendorCreditRet",  "VendorCreditAddRq",  "VendorCreditAdd",  true),
            ["InventoryAdjustment"] = ("InventoryAdjustmentQueryRq", "InventoryAdjustmentRet", "InventoryAdjustmentAddRq", "InventoryAdjustmentAdd", true),
            ["Transfer"]      = ("TransferQueryRq",      "TransferRet",      "TransferAddRq",      "TransferAdd",      true),
            
            // Credit Card transactions - testing if QB 2021 SDK 15.0 supports these
            ["CreditCardCharge"] = ("CreditCardChargeQueryRq", "CreditCardChargeRet", "CreditCardChargeAddRq", "CreditCardChargeAdd", true),
            ["CreditCardCredit"] = ("CreditCardCreditQueryRq", "CreditCardCreditRet", "CreditCardCreditAddRq", "CreditCardCreditAdd", true),
        };

        /// <summary>
        /// Known field schemas for common QuickBooks entities. These define the expected structure
        /// when the SDK cannot be queried directly (e.g., empty company file with no data).
        /// Based on QuickBooks SDK 15.0 (QB 2021) documentation.
        /// </summary>
        private static readonly Dictionary<string, List<QBFieldSchema>> KnownSchemas = new()
        {
            ["Customer"] = new List<QBFieldSchema>
            {
                new() { FieldName = "ListID", DataType = "IDTYPE", IsRequired = false, IsReadOnly = true, Description = "Unique QB identifier" },
                new() { FieldName = "TimeCreated", DataType = "DATETIMETYPE", IsRequired = false, IsReadOnly = true },
                new() { FieldName = "TimeModified", DataType = "DATETIMETYPE", IsRequired = false, IsReadOnly = true },
                new() { FieldName = "EditSequence", DataType = "STRTYPE", IsRequired = false, IsReadOnly = true },
                new() { FieldName = "Name", DataType = "STRTYPE", MaxLength = 41, IsRequired = true, Description = "Customer name (unique)" },
                new() { FieldName = "FullName", DataType = "STRTYPE", MaxLength = 209, IsRequired = false, IsReadOnly = true },
                new() { FieldName = "IsActive", DataType = "BOOLTYPE", IsRequired = false },
                new() { FieldName = "CompanyName", DataType = "STRTYPE", MaxLength = 41, IsRequired = false },
                new() { FieldName = "Salutation", DataType = "STRTYPE", MaxLength = 15, IsRequired = false },
                new() { FieldName = "FirstName", DataType = "STRTYPE", MaxLength = 25, IsRequired = false },
                new() { FieldName = "MiddleName", DataType = "STRTYPE", MaxLength = 5, IsRequired = false },
                new() { FieldName = "LastName", DataType = "STRTYPE", MaxLength = 25, IsRequired = false },
                new() { FieldName = "BillAddress.Addr1", DataType = "STRTYPE", MaxLength = 41, IsRequired = false, ParentField = "BillAddress" },
                new() { FieldName = "BillAddress.Addr2", DataType = "STRTYPE", MaxLength = 41, IsRequired = false, ParentField = "BillAddress" },
                new() { FieldName = "BillAddress.Addr3", DataType = "STRTYPE", MaxLength = 41, IsRequired = false, ParentField = "BillAddress" },
                new() { FieldName = "BillAddress.Addr4", DataType = "STRTYPE", MaxLength = 41, IsRequired = false, ParentField = "BillAddress" },
                new() { FieldName = "BillAddress.Addr5", DataType = "STRTYPE", MaxLength = 41, IsRequired = false, ParentField = "BillAddress" },
                new() { FieldName = "BillAddress.City", DataType = "STRTYPE", MaxLength = 31, IsRequired = false, ParentField = "BillAddress" },
                new() { FieldName = "BillAddress.State", DataType = "STRTYPE", MaxLength = 21, IsRequired = false, ParentField = "BillAddress" },
                new() { FieldName = "BillAddress.PostalCode", DataType = "STRTYPE", MaxLength = 13, IsRequired = false, ParentField = "BillAddress" },
                new() { FieldName = "BillAddress.Country", DataType = "STRTYPE", MaxLength = 31, IsRequired = false, ParentField = "BillAddress" },
                new() { FieldName = "ShipAddress.Addr1", DataType = "STRTYPE", MaxLength = 41, IsRequired = false, ParentField = "ShipAddress" },
                new() { FieldName = "ShipAddress.City", DataType = "STRTYPE", MaxLength = 31, IsRequired = false, ParentField = "ShipAddress" },
                new() { FieldName = "ShipAddress.State", DataType = "STRTYPE", MaxLength = 21, IsRequired = false, ParentField = "ShipAddress" },
                new() { FieldName = "ShipAddress.PostalCode", DataType = "STRTYPE", MaxLength = 13, IsRequired = false, ParentField = "ShipAddress" },
                new() { FieldName = "Phone", DataType = "STRTYPE", MaxLength = 21, IsRequired = false },
                new() { FieldName = "AltPhone", DataType = "STRTYPE", MaxLength = 21, IsRequired = false },
                new() { FieldName = "Fax", DataType = "STRTYPE", MaxLength = 21, IsRequired = false },
                new() { FieldName = "Email", DataType = "STRTYPE", MaxLength = 1023, IsRequired = false },
                new() { FieldName = "Contact", DataType = "STRTYPE", MaxLength = 41, IsRequired = false },
                new() { FieldName = "AccountNumber", DataType = "STRTYPE", MaxLength = 99, IsRequired = false },
                new() { FieldName = "CreditLimit", DataType = "AMTTYPE", IsRequired = false },
                new() { FieldName = "TermsRef.FullName", DataType = "STRTYPE", MaxLength = 31, IsRequired = false },
                new() { FieldName = "SalesRepRef.FullName", DataType = "STRTYPE", MaxLength = 5, IsRequired = false },
                new() { FieldName = "Balance", DataType = "AMTTYPE", IsRequired = false, IsReadOnly = true },
                new() { FieldName = "TotalBalance", DataType = "AMTTYPE", IsRequired = false, IsReadOnly = true },
                new() { FieldName = "SalesTaxCodeRef.FullName", DataType = "STRTYPE", IsRequired = false },
                new() { FieldName = "ItemSalesTaxRef.FullName", DataType = "STRTYPE", IsRequired = false },
                new() { FieldName = "PreferredPaymentMethodRef.FullName", DataType = "STRTYPE", IsRequired = false },
                new() { FieldName = "JobStatus", DataType = "ENUMTYPE", IsRequired = false, AllowedValues = new() { "Awarded", "Closed", "InProgress", "None", "NotAwarded", "Pending" } },
                new() { FieldName = "Notes", DataType = "STRTYPE", MaxLength = 4095, IsRequired = false },
                new() { FieldName = "PriceLevelRef.FullName", DataType = "STRTYPE", IsRequired = false },
                new() { FieldName = "CurrencyRef.FullName", DataType = "STRTYPE", IsRequired = false },
            },
            ["Vendor"] = new List<QBFieldSchema>
            {
                new() { FieldName = "ListID", DataType = "IDTYPE", IsReadOnly = true },
                new() { FieldName = "Name", DataType = "STRTYPE", MaxLength = 41, IsRequired = true },
                new() { FieldName = "IsActive", DataType = "BOOLTYPE" },
                new() { FieldName = "CompanyName", DataType = "STRTYPE", MaxLength = 41 },
                new() { FieldName = "FirstName", DataType = "STRTYPE", MaxLength = 25 },
                new() { FieldName = "LastName", DataType = "STRTYPE", MaxLength = 25 },
                new() { FieldName = "VendorAddress.Addr1", DataType = "STRTYPE", MaxLength = 41, ParentField = "VendorAddress" },
                new() { FieldName = "VendorAddress.City", DataType = "STRTYPE", MaxLength = 31, ParentField = "VendorAddress" },
                new() { FieldName = "VendorAddress.State", DataType = "STRTYPE", MaxLength = 21, ParentField = "VendorAddress" },
                new() { FieldName = "VendorAddress.PostalCode", DataType = "STRTYPE", MaxLength = 13, ParentField = "VendorAddress" },
                new() { FieldName = "Phone", DataType = "STRTYPE", MaxLength = 21 },
                new() { FieldName = "Email", DataType = "STRTYPE", MaxLength = 1023 },
                new() { FieldName = "AccountNumber", DataType = "STRTYPE", MaxLength = 99 },
                new() { FieldName = "TermsRef.FullName", DataType = "STRTYPE" },
                new() { FieldName = "CreditLimit", DataType = "AMTTYPE" },
                new() { FieldName = "TaxIdent", DataType = "STRTYPE", MaxLength = 20 },
                new() { FieldName = "IsVendorEligibleFor1099", DataType = "BOOLTYPE" },
                new() { FieldName = "Balance", DataType = "AMTTYPE", IsReadOnly = true },
                new() { FieldName = "Notes", DataType = "STRTYPE", MaxLength = 4095 },
            },
            ["Account"] = new List<QBFieldSchema>
            {
                new() { FieldName = "ListID", DataType = "IDTYPE", IsReadOnly = true },
                new() { FieldName = "Name", DataType = "STRTYPE", MaxLength = 31, IsRequired = true },
                new() { FieldName = "FullName", DataType = "STRTYPE", MaxLength = 159, IsReadOnly = true },
                new() { FieldName = "IsActive", DataType = "BOOLTYPE" },
                new() { FieldName = "AccountType", DataType = "ENUMTYPE", IsRequired = true,
                    AllowedValues = new() { "AccountsPayable", "AccountsReceivable", "Bank", "CostOfGoodsSold",
                        "CreditCard", "Equity", "Expense", "FixedAsset", "Income", "LongTermLiability",
                        "NonPosting", "OtherAsset", "OtherCurrentAsset", "OtherCurrentLiability",
                        "OtherExpense", "OtherIncome" } },
                new() { FieldName = "AccountNumber", DataType = "STRTYPE", MaxLength = 7 },
                new() { FieldName = "Description", DataType = "STRTYPE", MaxLength = 200 },
                new() { FieldName = "BankNumber", DataType = "STRTYPE", MaxLength = 25 },
                new() { FieldName = "OpenBalance", DataType = "AMTTYPE" },
                new() { FieldName = "OpenBalanceDate", DataType = "DATETYPE" },
                new() { FieldName = "ParentRef.FullName", DataType = "STRTYPE" },
                new() { FieldName = "SalesTaxCodeRef.FullName", DataType = "STRTYPE" },
                new() { FieldName = "TaxLineID", DataType = "INTTYPE" },
                new() { FieldName = "Balance", DataType = "AMTTYPE", IsReadOnly = true },
                new() { FieldName = "TotalBalance", DataType = "AMTTYPE", IsReadOnly = true },
                new() { FieldName = "CurrencyRef.FullName", DataType = "STRTYPE" },
            },
            ["Invoice"] = new List<QBFieldSchema>
            {
                new() { FieldName = "TxnID", DataType = "IDTYPE", IsReadOnly = true },
                new() { FieldName = "TxnNumber", DataType = "INTTYPE", IsReadOnly = true },
                new() { FieldName = "CustomerRef.FullName", DataType = "STRTYPE", IsRequired = true },
                new() { FieldName = "ClassRef.FullName", DataType = "STRTYPE" },
                new() { FieldName = "ARAccountRef.FullName", DataType = "STRTYPE" },
                new() { FieldName = "TemplateRef.FullName", DataType = "STRTYPE" },
                new() { FieldName = "TxnDate", DataType = "DATETYPE", IsRequired = true },
                new() { FieldName = "RefNumber", DataType = "STRTYPE", MaxLength = 11 },
                new() { FieldName = "BillAddress.Addr1", DataType = "STRTYPE", MaxLength = 41 },
                new() { FieldName = "ShipAddress.Addr1", DataType = "STRTYPE", MaxLength = 41 },
                new() { FieldName = "IsPending", DataType = "BOOLTYPE" },
                new() { FieldName = "PONumber", DataType = "STRTYPE", MaxLength = 25 },
                new() { FieldName = "TermsRef.FullName", DataType = "STRTYPE" },
                new() { FieldName = "DueDate", DataType = "DATETYPE" },
                new() { FieldName = "SalesRepRef.FullName", DataType = "STRTYPE" },
                new() { FieldName = "ShipMethodRef.FullName", DataType = "STRTYPE" },
                new() { FieldName = "ShipDate", DataType = "DATETYPE" },
                new() { FieldName = "Memo", DataType = "STRTYPE", MaxLength = 4095 },
                new() { FieldName = "IsToBePrinted", DataType = "BOOLTYPE" },
                new() { FieldName = "IsToBeEmailed", DataType = "BOOLTYPE" },
                new() { FieldName = "Subtotal", DataType = "AMTTYPE", IsReadOnly = true },
                new() { FieldName = "BalanceRemaining", DataType = "AMTTYPE", IsReadOnly = true },
            },
        };

        public SchemaExtractor(QBConnectionManager connection, string sdkVersion, string outputDirectory)
        {
            _connection = connection;
            _sdkVersion = sdkVersion;
            _outputDirectory = outputDirectory;
        }

        /// <summary>
        /// Extracts schemas for all known entity types and saves to JSON.
        /// </summary>
        public QBSchemaExport ExtractAllSchemas()
        {
            Log.Information("Starting schema extraction for SDK version {Version}...", _sdkVersion);

            var schemaExport = new QBSchemaExport
            {
                QuickBooksVersion = _sdkVersion.StartsWith("15") ? "QB 2021" : "QB 2023",
                SDKVersion = _sdkVersion,
                ExtractedAt = DateTime.UtcNow
            };

            foreach (var (entityName, definition) in EntityDefinitions)
            {
                try
                {
                    Log.Information("Extracting schema for: {Entity}", entityName);
                    var schema = ExtractEntitySchema(entityName, definition);
                    schemaExport.EntitySchemas[entityName] = schema;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Could not extract schema for {Entity}: {Message}",
                        entityName, ex.Message);

                    // Fall back to known schema if available
                    if (KnownSchemas.ContainsKey(entityName))
                    {
                        Log.Information("Using built-in schema definition for {Entity}", entityName);
                        schemaExport.EntitySchemas[entityName] = new QBEntitySchema
                        {
                            EntityType = entityName,
                            QBXMLRequestType = definition.QueryRq,
                            QBXMLResponseType = definition.ResponseRet,
                            SDKVersion = _sdkVersion,
                            Fields = KnownSchemas[entityName]
                        };
                    }
                }
            }

            // Save schema to file
            SaveSchema(schemaExport);

            Log.Information("Schema extraction complete. {Count} entity types documented.",
                schemaExport.EntitySchemas.Count);

            return schemaExport;
        }

        /// <summary>
        /// Extracts the schema for a single entity type by querying QB and analyzing the response structure.
        /// </summary>
        private QBEntitySchema ExtractEntitySchema(string entityName,
            (string QueryRq, string ResponseRet, string AddRq, string AddType, bool IsTransaction) definition)
        {
            var schema = new QBEntitySchema
            {
                EntityType = entityName,
                QBXMLRequestType = definition.QueryRq,
                QBXMLResponseType = definition.ResponseRet,
                SDKVersion = _sdkVersion
            };

            // First try to get schema from known definitions
            if (KnownSchemas.ContainsKey(entityName))
            {
                schema.Fields = KnownSchemas[entityName];
                return schema;
            }

            // Try querying QB for a sample record to discover fields
            if (_connection.IsConnected)
            {
                try
                {
                    var queryXml = QBConnectionManager.BuildQueryRequest(
                        definition.QueryRq.Replace("Rq", ""),
                        _sdkVersion,
                        includeInactive: true,
                        maxReturned: 1
                    );

                    var response = _connection.ProcessRequest(queryXml);
                    var entities = QBConnectionManager.ParseResponseEntities(response, definition.ResponseRet);

                    if (entities.Any())
                    {
                        schema.Fields = DiscoverFieldsFromXml(entities.First(), "");
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not query sample data for {Entity}: {Message}", entityName, ex.Message);
                }
            }

            return schema;
        }

        /// <summary>
        /// Discovers field schemas by analyzing an XML response element recursively.
        /// </summary>
        private List<QBFieldSchema> DiscoverFieldsFromXml(XElement element, string prefix)
        {
            var fields = new List<QBFieldSchema>();

            foreach (var child in element.Elements())
            {
                var fieldName = string.IsNullOrEmpty(prefix) ? child.Name.LocalName : $"{prefix}.{child.Name.LocalName}";

                if (child.HasElements)
                {
                    // This is a complex/nested element - recurse
                    fields.AddRange(DiscoverFieldsFromXml(child, fieldName));
                }
                else
                {
                    // This is a leaf element - it's a field
                    var field = new QBFieldSchema
                    {
                        FieldName = fieldName,
                        DataType = InferDataType(child.Value),
                        IsRequired = false, // Can't determine from response alone
                        IsReadOnly = IsKnownReadOnlyField(fieldName),
                        ParentField = string.IsNullOrEmpty(prefix) ? null : prefix
                    };
                    fields.Add(field);
                }
            }

            return fields;
        }

        /// <summary>
        /// Infers the QBXML data type from a sample value.
        /// </summary>
        private static string InferDataType(string value)
        {
            if (string.IsNullOrEmpty(value)) return "STRTYPE";
            if (bool.TryParse(value, out _)) return "BOOLTYPE";
            if (int.TryParse(value, out _)) return "INTTYPE";
            if (decimal.TryParse(value, out _)) return "AMTTYPE";
            if (DateTime.TryParse(value, out _))
            {
                return value.Contains("T") ? "DATETIMETYPE" : "DATETYPE";
            }
            if (value.StartsWith("{") && value.EndsWith("}")) return "IDTYPE"; // GUID format
            return "STRTYPE";
        }

        /// <summary>
        /// Checks if a field is known to be read-only (system-generated).
        /// </summary>
        private static bool IsKnownReadOnlyField(string fieldName)
        {
            var readOnlyFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ListID", "TxnID", "TimeCreated", "TimeModified", "EditSequence",
                "FullName", "TxnNumber", "Balance", "TotalBalance", "Subtotal",
                "BalanceRemaining", "IsPaid", "OpenBalance"
            };

            var leafName = fieldName.Contains('.') ? fieldName.Split('.').Last() : fieldName;
            return readOnlyFields.Contains(leafName);
        }

        /// <summary>
        /// Saves the extracted schema to a JSON file.
        /// </summary>
        private void SaveSchema(QBSchemaExport schema)
        {
            Directory.CreateDirectory(_outputDirectory);
            var filePath = Path.Combine(_outputDirectory, $"QB_Schema_{schema.QuickBooksVersion.Replace(" ", "_")}.json");

            var json = JsonConvert.SerializeObject(schema, Formatting.Indented);
            File.WriteAllText(filePath, json);

            Log.Information("Schema saved to: {FilePath}", filePath);
        }

        /// <summary>
        /// Gets the entity definitions dictionary (useful for other services).
        /// </summary>
        public static Dictionary<string, (string QueryRq, string ResponseRet, string AddRq, string AddType, bool IsTransaction)>
            GetEntityDefinitions() => EntityDefinitions;
    }
}
