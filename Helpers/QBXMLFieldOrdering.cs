using Serilog;

namespace QB_TimeWarp.Helpers
{
    /// <summary>
    /// Defines the correct QBXML XSD element ordering for each entity type.
    /// 
    /// CRITICAL: QBXML uses xs:sequence in its schema, meaning elements MUST appear
    /// in the exact order specified by the XSD. QuickBooks Desktop will reject any
    /// request with out-of-order elements with error 0x80040400:
    ///   "QuickBooks found an error when parsing the provided XML text stream."
    /// 
    /// The ordering maps below are derived from:
    ///   - Intuit QBXML SDK documentation (qbxmlops20.xml schema)
    ///   - QuickBooks PHP SDK schema definitions (consolibyte/quickbooks-php)
    ///   - Empirical testing against QB Desktop 2021 (SDK 15.0)
    /// 
    /// Each dictionary maps field names to their ordinal position in the XSD sequence.
    /// Lower numbers = earlier in XML output. Fields not in the map get index 9999
    /// and are appended at the end (alphabetically as a tiebreaker).
    /// </summary>
    public static class QBXMLFieldOrdering
    {
        // =====================================================================
        // LIST ENTITY TYPES (Add operations)
        // =====================================================================

        /// <summary>
        /// CustomerAdd element order per QBXML XSD schema.
        /// Reference: qbxmlops20.xml — CustomerAddRq sequence.
        /// </summary>
        public static readonly Dictionary<string, int> CustomerAddOrder = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Name", 0 },
            { "IsActive", 1 },
            { "ParentRef", 2 },
            { "CompanyName", 3 },
            { "Salutation", 4 },
            { "FirstName", 5 },
            { "MiddleName", 6 },
            { "LastName", 7 },
            { "Suffix", 8 },
            { "BillAddress", 10 },
            { "ShipAddress", 11 },
            { "ShipToAddress", 12 },
            { "PrintAs", 13 },
            { "Phone", 14 },
            { "Mobile", 15 },
            { "Pager", 16 },
            { "AltPhone", 17 },
            { "Fax", 18 },
            { "Email", 19 },
            { "Cc", 20 },
            { "Contact", 21 },
            { "AltContact", 22 },
            { "AdditionalContactRef", 23 },
            { "ContactsRet", 24 },
            { "CustomerTypeRef", 25 },
            { "TermsRef", 26 },
            { "SalesRepRef", 27 },
            { "OpenBalance", 28 },
            { "OpenBalanceDate", 29 },
            { "SalesTaxCodeRef", 30 },
            { "ItemSalesTaxRef", 31 },
            { "SalesTaxCountry", 32 },
            { "ResaleNumber", 33 },
            { "AccountNumber", 34 },
            { "CreditLimit", 35 },
            { "PreferredPaymentMethodRef", 36 },
            { "CreditCardInfo", 37 },
            { "JobStatus", 38 },
            { "JobStartDate", 39 },
            { "JobProjectedEndDate", 40 },
            { "JobEndDate", 41 },
            { "JobDesc", 42 },
            { "JobTypeRef", 43 },
            { "Notes", 44 },
            { "AdditionalNotes", 45 },
            { "IsStatementWithParent", 46 },
            { "DeliveryMethod", 47 },
            { "PriceLevelRef", 48 },
            { "CurrencyRef", 49 },
        };

        /// <summary>
        /// VendorAdd element order per QBXML XSD schema.
        /// Reference: qbxmlops20.xml — VendorAddRq sequence.
        /// </summary>
        public static readonly Dictionary<string, int> VendorAddOrder = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Name", 0 },
            { "IsActive", 1 },
            { "CompanyName", 2 },
            { "Salutation", 3 },
            { "FirstName", 4 },
            { "MiddleName", 5 },
            { "LastName", 6 },
            { "Suffix", 7 },
            { "VendorAddress", 8 },
            { "ShipAddress", 9 },
            { "Phone", 10 },
            { "Mobile", 11 },
            { "Pager", 12 },
            { "AltPhone", 13 },
            { "Fax", 14 },
            { "Email", 15 },
            { "Cc", 16 },
            { "Contact", 17 },
            { "AltContact", 18 },
            { "AdditionalContactRef", 19 },
            { "ContactsRet", 20 },
            { "NameOnCheck", 21 },
            { "AccountNumber", 22 },
            { "Notes", 23 },
            { "AdditionalNotes", 24 },
            { "VendorTypeRef", 25 },
            { "TermsRef", 26 },
            { "CreditLimit", 27 },
            { "VendorTaxIdent", 28 },
            { "IsVendorEligibleFor1099", 29 },
            { "OpenBalance", 30 },
            { "OpenBalanceDate", 31 },
            { "BillingRateRef", 32 },
            { "PrefillAccountRef", 33 },
            { "CurrencyRef", 34 },
        };

        /// <summary>
        /// AccountAdd element order per QBXML XSD schema.
        /// Reference: qbxmlops20.xml — AccountAddRq sequence.
        /// </summary>
        public static readonly Dictionary<string, int> AccountAddOrder = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Name", 0 },
            { "IsActive", 1 },
            { "ParentRef", 2 },
            { "AccountType", 3 },
            { "DetailAccountType", 4 },
            { "AccountNumber", 5 },
            { "BankNumber", 6 },
            { "Desc", 7 },
            { "Description", 7 },  // Alias — some exports use "Description" instead of "Desc"
            { "OpenBalance", 8 },
            { "OpenBalanceDate", 9 },
            { "SalesTaxCodeRef", 10 },
            { "TaxLineID", 11 },
            { "CurrencyRef", 12 },
        };

        /// <summary>
        /// EmployeeAdd element order per QBXML XSD schema.
        /// </summary>
        public static readonly Dictionary<string, int> EmployeeAddOrder = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Name", 0 },
            { "IsActive", 1 },
            { "Salutation", 2 },
            { "FirstName", 3 },
            { "MiddleName", 4 },
            { "LastName", 5 },
            { "Suffix", 6 },
            { "EmployeeAddress", 7 },
            { "PrintAs", 8 },
            { "Phone", 9 },
            { "Mobile", 10 },
            { "Pager", 11 },
            { "AltPhone", 12 },
            { "Fax", 13 },
            { "Email", 14 },
            { "SSN", 15 },
            { "EmployeeType", 16 },
            { "Gender", 17 },
            { "HiredDate", 18 },
            { "ReleasedDate", 19 },
            { "BirthDate", 20 },
            { "AccountNumber", 21 },
            { "Notes", 22 },
            { "BillingRateRef", 23 },
            { "EmployeePayrollInfo", 24 },
        };

        /// <summary>
        /// ClassAdd element order per QBXML XSD schema.
        /// </summary>
        public static readonly Dictionary<string, int> ClassAddOrder = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Name", 0 },
            { "IsActive", 1 },
            { "ParentRef", 2 },
        };

        /// <summary>
        /// StandardTermsAdd element order.
        /// </summary>
        public static readonly Dictionary<string, int> StandardTermsAddOrder = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Name", 0 },
            { "IsActive", 1 },
            { "StdDueDays", 2 },
            { "StdDiscountDays", 3 },
            { "DiscountPct", 4 },
        };

        /// <summary>
        /// PaymentMethodAdd element order.
        /// </summary>
        public static readonly Dictionary<string, int> PaymentMethodAddOrder = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Name", 0 },
            { "IsActive", 1 },
            { "PaymentMethodType", 2 },
        };

        /// <summary>
        /// SalesTaxCodeAdd element order.
        /// </summary>
        public static readonly Dictionary<string, int> SalesTaxCodeAddOrder = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Name", 0 },
            { "IsActive", 1 },
            { "IsTaxable", 2 },
            { "Desc", 3 },
            { "Description", 3 },
        };

        /// <summary>
        /// ShipMethodAdd element order.
        /// </summary>
        public static readonly Dictionary<string, int> ShipMethodAddOrder = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Name", 0 },
            { "IsActive", 1 },
        };

        /// <summary>
        /// CustomerTypeAdd element order.
        /// </summary>
        public static readonly Dictionary<string, int> CustomerTypeAddOrder = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Name", 0 },
            { "IsActive", 1 },
            { "ParentRef", 2 },
        };

        /// <summary>
        /// VendorTypeAdd element order.
        /// </summary>
        public static readonly Dictionary<string, int> VendorTypeAddOrder = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Name", 0 },
            { "IsActive", 1 },
            { "ParentRef", 2 },
        };

        /// <summary>
        /// JobTypeAdd element order.
        /// </summary>
        public static readonly Dictionary<string, int> JobTypeAddOrder = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Name", 0 },
            { "IsActive", 1 },
            { "ParentRef", 2 },
        };

        /// <summary>
        /// PriceLevelAdd element order.
        /// </summary>
        public static readonly Dictionary<string, int> PriceLevelAddOrder = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Name", 0 },
            { "IsActive", 1 },
            { "PriceLevelType", 2 },
            { "PriceLevelFixedPercentage", 3 },
            { "PriceLevelPerItem", 4 },
        };

        // =====================================================================
        // ITEM ENTITY TYPES (Add operations)
        // =====================================================================

        /// <summary>
        /// ItemServiceAdd element order per QBXML XSD.
        /// </summary>
        public static readonly Dictionary<string, int> ItemServiceAddOrder = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Name", 0 },
            { "BarCode", 1 },
            { "IsActive", 2 },
            { "ParentRef", 3 },
            { "ClassRef", 4 },
            { "UnitOfMeasureSetRef", 5 },
            { "IsTaxIncluded", 6 },
            { "SalesTaxCodeRef", 7 },
            { "SalesOrPurchase", 8 },
            { "SalesAndPurchase", 9 },
        };

        /// <summary>
        /// ItemInventoryAdd element order per QBXML XSD.
        /// </summary>
        public static readonly Dictionary<string, int> ItemInventoryAddOrder = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Name", 0 },
            { "BarCode", 1 },
            { "IsActive", 2 },
            { "ParentRef", 3 },
            { "ClassRef", 4 },
            { "UnitOfMeasureSetRef", 5 },
            { "IsTaxIncluded", 6 },
            { "SalesTaxCodeRef", 7 },
            { "SalesDesc", 8 },
            { "SalesPrice", 9 },
            { "IncomeAccountRef", 10 },
            { "PurchaseDesc", 11 },
            { "PurchaseCost", 12 },
            { "PurchaseTaxCodeRef", 13 },
            { "COGSAccountRef", 14 },
            { "PrefVendorRef", 15 },
            { "AssetAccountRef", 16 },
            { "ReorderPoint", 17 },
            { "Max", 18 },
            { "QuantityOnHand", 19 },
            { "TotalValue", 20 },
            { "InventoryDate", 21 },
        };

        /// <summary>
        /// ItemNonInventoryAdd element order per QBXML XSD.
        /// </summary>
        public static readonly Dictionary<string, int> ItemNonInventoryAddOrder = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Name", 0 },
            { "BarCode", 1 },
            { "IsActive", 2 },
            { "ParentRef", 3 },
            { "ClassRef", 4 },
            { "UnitOfMeasureSetRef", 5 },
            { "IsTaxIncluded", 6 },
            { "SalesTaxCodeRef", 7 },
            { "SalesOrPurchase", 8 },
            { "SalesAndPurchase", 9 },
        };

        /// <summary>
        /// ItemOtherChargeAdd element order per QBXML XSD.
        /// </summary>
        public static readonly Dictionary<string, int> ItemOtherChargeAddOrder = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Name", 0 },
            { "BarCode", 1 },
            { "IsActive", 2 },
            { "ParentRef", 3 },
            { "ClassRef", 4 },
            { "IsTaxIncluded", 5 },
            { "SalesTaxCodeRef", 6 },
            { "SalesOrPurchase", 7 },
            { "SalesAndPurchase", 8 },
        };

        /// <summary>
        /// ItemDiscountAdd element order per QBXML XSD.
        /// </summary>
        public static readonly Dictionary<string, int> ItemDiscountAddOrder = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Name", 0 },
            { "BarCode", 1 },
            { "IsActive", 2 },
            { "ParentRef", 3 },
            { "ClassRef", 4 },
            { "IsTaxIncluded", 5 },
            { "SalesTaxCodeRef", 6 },
            { "DiscountRate", 7 },
            { "DiscountRatePercent", 8 },
            { "AccountRef", 9 },
        };

        /// <summary>
        /// ItemSalesTaxAdd element order per QBXML XSD.
        /// </summary>
        public static readonly Dictionary<string, int> ItemSalesTaxAddOrder = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Name", 0 },
            { "BarCode", 1 },
            { "IsActive", 2 },
            { "ClassRef", 3 },
            { "ItemDesc", 4 },
            { "TaxRate", 5 },
            { "TaxVendorRef", 6 },
            { "SalesTaxReturnLineRef", 7 },
        };

        /// <summary>
        /// ItemGroupAdd element order per QBXML XSD.
        /// </summary>
        public static readonly Dictionary<string, int> ItemGroupAddOrder = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Name", 0 },
            { "BarCode", 1 },
            { "IsActive", 2 },
            { "ItemDesc", 3 },
            { "UnitOfMeasureSetRef", 4 },
            { "IsPrintItemsInGroup", 5 },
            { "ItemGroupLine", 6 },
        };

        // =====================================================================
        // TRANSACTION ENTITY TYPES (Add operations)
        // =====================================================================

        /// <summary>
        /// InvoiceAdd element order per QBXML XSD schema.
        /// Reference: qbxmlops20.xml — InvoiceAddRq sequence.
        /// </summary>
        public static readonly Dictionary<string, int> InvoiceAddOrder = new(StringComparer.OrdinalIgnoreCase)
        {
            { "CustomerRef", 0 },
            { "ClassRef", 1 },
            { "ARAccountRef", 2 },
            { "TemplateRef", 3 },
            { "TxnDate", 4 },
            { "RefNumber", 5 },
            { "BillAddress", 6 },
            { "ShipAddress", 7 },
            { "IsPending", 8 },
            { "IsFinanceCharge", 9 },
            { "PONumber", 10 },
            { "TermsRef", 11 },
            { "DueDate", 12 },
            { "SalesRepRef", 13 },
            { "FOB", 14 },
            { "ShipDate", 15 },
            { "ShipMethodRef", 16 },
            { "ItemSalesTaxRef", 17 },
            { "Memo", 18 },
            { "CustomerMsgRef", 19 },
            { "IsToBePrinted", 20 },
            { "IsToBeEmailed", 21 },
            { "IsTaxIncluded", 22 },
            { "CustomerSalesTaxCodeRef", 23 },
            { "Other", 24 },
            { "ExchangeRate", 25 },
            { "ExternalGUID", 26 },
            { "LinkToTxnID", 27 },
            // Line items handled separately by BuildLineItemsXml
            { "InvoiceLineAdd", 100 },
            { "InvoiceLineGroupAdd", 101 },
            { "DiscountLineAdd", 102 },
            { "SalesTaxLineAdd", 103 },
            { "ShippingLineAdd", 104 },
        };

        /// <summary>
        /// BillAdd element order per QBXML XSD schema.
        /// Reference: consolibyte/quickbooks-php BillAddRq schema.
        /// </summary>
        public static readonly Dictionary<string, int> BillAddOrder = new(StringComparer.OrdinalIgnoreCase)
        {
            { "VendorRef", 0 },
            { "VendorAddress", 1 },
            { "APAccountRef", 2 },
            { "TxnDate", 3 },
            { "DueDate", 4 },
            { "RefNumber", 5 },
            { "TermsRef", 6 },
            { "Memo", 7 },
            { "IsTaxIncluded", 8 },
            { "SalesTaxCodeRef", 9 },
            { "ExchangeRate", 10 },
            { "ExternalGUID", 11 },
            { "LinkToTxnID", 12 },
            // Line items handled separately
            { "ExpenseLineAdd", 100 },
            { "ItemLineAdd", 101 },
            { "ItemGroupLineAdd", 102 },
        };

        /// <summary>
        /// CheckAdd element order per QBXML XSD schema.
        /// Reference: consolibyte/quickbooks-php CheckAddRq schema.
        /// </summary>
        public static readonly Dictionary<string, int> CheckAddOrder = new(StringComparer.OrdinalIgnoreCase)
        {
            { "AccountRef", 0 },
            { "PayeeEntityRef", 1 },
            { "RefNumber", 2 },
            { "TxnDate", 3 },
            { "Memo", 4 },
            { "Address", 5 },
            { "IsToBePrinted", 6 },
            { "IsTaxIncluded", 7 },
            { "SalesTaxCodeRef", 8 },
            { "ExchangeRate", 9 },
            { "ExternalGUID", 10 },
            { "ApplyCheckToTxnAdd", 11 },
            // Line items handled separately
            { "ExpenseLineAdd", 100 },
            { "ItemLineAdd", 101 },
            { "ItemGroupLineAdd", 102 },
        };

        /// <summary>
        /// DepositAdd element order per QBXML XSD schema.
        /// Reference: consolibyte/quickbooks-php DepositAddRq schema.
        /// </summary>
        public static readonly Dictionary<string, int> DepositAddOrder = new(StringComparer.OrdinalIgnoreCase)
        {
            { "TxnDate", 0 },
            { "DepositToAccountRef", 1 },
            { "Memo", 2 },
            { "CashBackInfoAdd", 3 },
            { "CurrencyRef", 4 },
            { "ExchangeRate", 5 },
            { "ExternalGUID", 6 },
            // Line items handled separately
            { "DepositLineAdd", 100 },
        };

        /// <summary>
        /// JournalEntryAdd element order per QBXML XSD schema.
        /// Reference: consolibyte/quickbooks-php JournalEntryAddRq schema.
        /// </summary>
        public static readonly Dictionary<string, int> JournalEntryAddOrder = new(StringComparer.OrdinalIgnoreCase)
        {
            { "TxnDate", 0 },
            { "RefNumber", 1 },
            { "Memo", 2 },
            { "IsAdjustment", 3 },
            { "CurrencyRef", 4 },
            { "ExchangeRate", 5 },
            { "ExternalGUID", 6 },
            // Line items handled separately
            { "JournalDebitLine", 100 },
            { "JournalCreditLine", 101 },
        };

        /// <summary>
        /// ReceivePaymentAdd element order per QBXML XSD schema.
        /// Reference: consolibyte/quickbooks-php ReceivePaymentAddRq schema.
        /// </summary>
        public static readonly Dictionary<string, int> ReceivePaymentAddOrder = new(StringComparer.OrdinalIgnoreCase)
        {
            { "CustomerRef", 0 },
            { "ARAccountRef", 1 },
            { "TxnDate", 2 },
            { "RefNumber", 3 },
            { "TotalAmount", 4 },
            { "ExchangeRate", 5 },
            { "PaymentMethodRef", 6 },
            { "Memo", 7 },
            { "DepositToAccountRef", 8 },
            { "CreditCardTxnInfo", 9 },
            { "ExternalGUID", 10 },
            { "IsAutoApply", 11 },
            // Applied-to transactions
            { "AppliedToTxnAdd", 100 },
        };

        /// <summary>
        /// SalesReceiptAdd element order per QBXML XSD schema.
        /// </summary>
        public static readonly Dictionary<string, int> SalesReceiptAddOrder = new(StringComparer.OrdinalIgnoreCase)
        {
            { "CustomerRef", 0 },
            { "ClassRef", 1 },
            { "TemplateRef", 2 },
            { "TxnDate", 3 },
            { "RefNumber", 4 },
            { "BillAddress", 5 },
            { "ShipAddress", 6 },
            { "IsPending", 7 },
            { "CheckNumber", 8 },
            { "PaymentMethodRef", 9 },
            { "DueDate", 10 },
            { "SalesRepRef", 11 },
            { "ShipDate", 12 },
            { "ShipMethodRef", 13 },
            { "FOB", 14 },
            { "DepositToAccountRef", 15 },
            { "ItemSalesTaxRef", 16 },
            { "Memo", 17 },
            { "CustomerMsgRef", 18 },
            { "IsToBePrinted", 19 },
            { "IsToBeEmailed", 20 },
            { "IsTaxIncluded", 21 },
            { "CustomerSalesTaxCodeRef", 22 },
            { "Other", 23 },
            { "ExchangeRate", 24 },
            { "ExternalGUID", 25 },
            // Line items handled separately
            { "SalesReceiptLineAdd", 100 },
            { "SalesReceiptLineGroupAdd", 101 },
            { "DiscountLineAdd", 102 },
            { "SalesTaxLineAdd", 103 },
            { "ShippingLineAdd", 104 },
        };

        /// <summary>
        /// CreditMemoAdd element order per QBXML XSD schema.
        /// </summary>
        public static readonly Dictionary<string, int> CreditMemoAddOrder = new(StringComparer.OrdinalIgnoreCase)
        {
            { "CustomerRef", 0 },
            { "ClassRef", 1 },
            { "ARAccountRef", 2 },
            { "TemplateRef", 3 },
            { "TxnDate", 4 },
            { "RefNumber", 5 },
            { "BillAddress", 6 },
            { "ShipAddress", 7 },
            { "IsPending", 8 },
            { "PONumber", 9 },
            { "TermsRef", 10 },
            { "DueDate", 11 },
            { "SalesRepRef", 12 },
            { "FOB", 13 },
            { "ShipDate", 14 },
            { "ShipMethodRef", 15 },
            { "ItemSalesTaxRef", 16 },
            { "Memo", 17 },
            { "CustomerMsgRef", 18 },
            { "IsToBePrinted", 19 },
            { "IsToBeEmailed", 20 },
            { "IsTaxIncluded", 21 },
            { "CustomerSalesTaxCodeRef", 22 },
            { "Other", 23 },
            { "ExchangeRate", 24 },
            { "ExternalGUID", 25 },
            // Line items handled separately
            { "CreditMemoLineAdd", 100 },
            { "CreditMemoLineGroupAdd", 101 },
            { "DiscountLineAdd", 102 },
            { "SalesTaxLineAdd", 103 },
            { "ShippingLineAdd", 104 },
        };

        /// <summary>
        /// EstimateAdd element order per QBXML XSD schema.
        /// </summary>
        public static readonly Dictionary<string, int> EstimateAddOrder = new(StringComparer.OrdinalIgnoreCase)
        {
            { "CustomerRef", 0 },
            { "ClassRef", 1 },
            { "TemplateRef", 2 },
            { "TxnDate", 3 },
            { "RefNumber", 4 },
            { "BillAddress", 5 },
            { "ShipAddress", 6 },
            { "IsActive", 7 },
            { "PONumber", 8 },
            { "TermsRef", 9 },
            { "DueDate", 10 },
            { "SalesRepRef", 11 },
            { "FOB", 12 },
            { "ItemSalesTaxRef", 13 },
            { "Memo", 14 },
            { "CustomerMsgRef", 15 },
            { "IsToBeEmailed", 16 },
            { "IsTaxIncluded", 17 },
            { "CustomerSalesTaxCodeRef", 18 },
            { "Other", 19 },
            { "ExchangeRate", 20 },
            { "ExternalGUID", 21 },
            // Line items handled separately
            { "EstimateLineAdd", 100 },
            { "EstimateLineGroupAdd", 101 },
        };

        /// <summary>
        /// PurchaseOrderAdd element order per QBXML XSD schema.
        /// </summary>
        public static readonly Dictionary<string, int> PurchaseOrderAddOrder = new(StringComparer.OrdinalIgnoreCase)
        {
            { "VendorRef", 0 },
            { "ClassRef", 1 },
            { "InventorySiteRef", 2 },
            { "ShipToEntityRef", 3 },
            { "TemplateRef", 4 },
            { "TxnDate", 5 },
            { "RefNumber", 6 },
            { "VendorAddress", 7 },
            { "ShipAddress", 8 },
            { "TermsRef", 9 },
            { "DueDate", 10 },
            { "ExpectedDate", 11 },
            { "ShipMethodRef", 12 },
            { "FOB", 13 },
            { "VendorMsg", 14 },
            { "IsManuallyClosed", 15 },
            { "Memo", 16 },
            { "IsToBePrinted", 17 },
            { "IsToBeEmailed", 18 },
            { "IsTaxIncluded", 19 },
            { "SalesTaxCodeRef", 20 },
            { "Other1", 21 },
            { "Other2", 22 },
            { "ExchangeRate", 23 },
            { "ExternalGUID", 24 },
            // Line items handled separately
            { "PurchaseOrderLineAdd", 100 },
            { "PurchaseOrderLineGroupAdd", 101 },
        };

        /// <summary>
        /// VendorCreditAdd element order per QBXML XSD schema.
        /// </summary>
        public static readonly Dictionary<string, int> VendorCreditAddOrder = new(StringComparer.OrdinalIgnoreCase)
        {
            { "VendorRef", 0 },
            { "APAccountRef", 1 },
            { "TxnDate", 2 },
            { "RefNumber", 3 },
            { "Memo", 4 },
            { "IsTaxIncluded", 5 },
            { "SalesTaxCodeRef", 6 },
            { "ExchangeRate", 7 },
            { "ExternalGUID", 8 },
            // Line items handled separately
            { "ExpenseLineAdd", 100 },
            { "ItemLineAdd", 101 },
            { "ItemGroupLineAdd", 102 },
        };

        /// <summary>
        /// InventoryAdjustmentAdd element order per QBXML XSD schema.
        /// </summary>
        public static readonly Dictionary<string, int> InventoryAdjustmentAddOrder = new(StringComparer.OrdinalIgnoreCase)
        {
            { "AccountRef", 0 },
            { "TxnDate", 1 },
            { "RefNumber", 2 },
            { "CustomerRef", 3 },
            { "ClassRef", 4 },
            { "Memo", 5 },
            { "ExternalGUID", 6 },
            // Line items handled separately
            { "InventoryAdjustmentLineAdd", 100 },
        };

        /// <summary>
        /// TransferAdd element order per QBXML XSD schema.
        /// </summary>
        public static readonly Dictionary<string, int> TransferAddOrder = new(StringComparer.OrdinalIgnoreCase)
        {
            { "TxnDate", 0 },
            { "TransferFromAccountRef", 1 },
            { "TransferToAccountRef", 2 },
            { "ClassRef", 3 },
            { "Amount", 4 },
            { "Memo", 5 },
        };

        // =====================================================================
        // ADDRESS BLOCK ORDERING (internal fields within address elements)
        // =====================================================================

        /// <summary>
        /// Address sub-element order (applies to BillAddress, ShipAddress,
        /// VendorAddress, EmployeeAddress, etc.).
        /// </summary>
        public static readonly Dictionary<string, int> AddressFieldOrder = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Addr1", 0 },
            { "Addr2", 1 },
            { "Addr3", 2 },
            { "Addr4", 3 },
            { "Addr5", 4 },
            { "City", 5 },
            { "State", 6 },
            { "Province", 7 },
            { "County", 8 },
            { "PostalCode", 9 },
            { "Country", 10 },
            { "Note", 11 },
        };

        // =====================================================================
        // LINE ITEM FIELD ORDERING (internal fields within line item elements)
        // =====================================================================

        /// <summary>
        /// InvoiceLineAdd sub-element order.
        /// </summary>
        public static readonly Dictionary<string, int> InvoiceLineAddFieldOrder = new(StringComparer.OrdinalIgnoreCase)
        {
            { "ItemRef", 0 },
            { "Desc", 1 },
            { "Quantity", 2 },
            { "UnitOfMeasure", 3 },
            { "Rate", 4 },
            { "RatePercent", 5 },
            { "PriceLevelRef", 6 },
            { "ClassRef", 7 },
            { "Amount", 8 },
            { "InventorySiteRef", 9 },
            { "InventorySiteLocationRef", 10 },
            { "ServiceDate", 11 },
            { "SalesTaxCodeRef", 12 },
            { "IsTaxable", 13 },
            { "OverrideItemAccountRef", 14 },
            { "Other1", 15 },
            { "Other2", 16 },
            { "LinkToTxn", 17 },
            { "DataExt", 18 },
        };

        /// <summary>
        /// ExpenseLineAdd sub-element order (used in Bills, Checks, etc.).
        /// </summary>
        public static readonly Dictionary<string, int> ExpenseLineAddFieldOrder = new(StringComparer.OrdinalIgnoreCase)
        {
            { "AccountRef", 0 },
            { "Amount", 1 },
            { "TaxAmount", 2 },
            { "Memo", 3 },
            { "CustomerRef", 4 },
            { "ClassRef", 5 },
            { "SalesTaxCodeRef", 6 },
            { "BillableStatus", 7 },
        };

        /// <summary>
        /// ItemLineAdd sub-element order (used in Bills, Checks, etc.).
        /// </summary>
        public static readonly Dictionary<string, int> ItemLineAddFieldOrder = new(StringComparer.OrdinalIgnoreCase)
        {
            { "ItemRef", 0 },
            { "InventorySiteRef", 1 },
            { "InventorySiteLocationRef", 2 },
            { "Desc", 3 },
            { "Quantity", 4 },
            { "UnitOfMeasure", 5 },
            { "Cost", 6 },
            { "Amount", 7 },
            { "TaxAmount", 8 },
            { "CustomerRef", 9 },
            { "ClassRef", 10 },
            { "SalesTaxCodeRef", 11 },
            { "BillableStatus", 12 },
            { "OverrideItemAccountRef", 13 },
            { "LinkToTxn", 14 },
        };

        /// <summary>
        /// JournalDebitLine / JournalCreditLine sub-element order.
        /// </summary>
        public static readonly Dictionary<string, int> JournalLineFieldOrder = new(StringComparer.OrdinalIgnoreCase)
        {
            { "TxnLineID", 0 },
            { "AccountRef", 1 },
            { "Amount", 2 },
            { "Memo", 3 },
            { "EntityRef", 4 },
            { "ClassRef", 5 },
            { "ItemSalesTaxRef", 6 },
            { "BillableStatus", 7 },
        };

        /// <summary>
        /// DepositLineAdd sub-element order.
        /// </summary>
        public static readonly Dictionary<string, int> DepositLineAddFieldOrder = new(StringComparer.OrdinalIgnoreCase)
        {
            { "PaymentTxnID", 0 },
            { "PaymentTxnLineID", 1 },
            { "OverrideMemo", 2 },
            { "OverrideCheckNumber", 3 },
            { "EntityRef", 4 },
            { "AccountRef", 5 },
            { "Memo", 6 },
            { "CheckNumber", 7 },
            { "PaymentMethodRef", 8 },
            { "ClassRef", 9 },
            { "Amount", 10 },
        };

        // =====================================================================
        // LOOKUP METHODS
        // =====================================================================

        /// <summary>
        /// Master mapping from entity type (as used in AddMap / ItemSubTypeMap)
        /// to the correct field ordering dictionary.
        /// Handles plural collection names (e.g., "Customers") and
        /// Add element names (e.g., "CustomerAdd").
        /// </summary>
        private static readonly Dictionary<string, Dictionary<string, int>> _entityOrderMap
            = new(StringComparer.OrdinalIgnoreCase)
        {
            // Collection names (plural, as used in AddMap keys)
            { "Accounts", AccountAddOrder },
            { "Customers", CustomerAddOrder },
            { "Vendors", VendorAddOrder },
            { "Employees", EmployeeAddOrder },
            { "Classes", ClassAddOrder },
            { "PaymentMethods", PaymentMethodAddOrder },
            { "Terms", StandardTermsAddOrder },
            { "SalesTaxCodes", SalesTaxCodeAddOrder },
            { "ShipMethods", ShipMethodAddOrder },
            { "CustomerTypes", CustomerTypeAddOrder },
            { "VendorTypes", VendorTypeAddOrder },
            { "JobTypes", JobTypeAddOrder },
            { "PriceLevels", PriceLevelAddOrder },
            { "Invoices", InvoiceAddOrder },
            { "Bills", BillAddOrder },
            { "Payments", ReceivePaymentAddOrder },
            { "SalesReceipts", SalesReceiptAddOrder },
            { "PurchaseOrders", PurchaseOrderAddOrder },
            { "JournalEntries", JournalEntryAddOrder },
            { "CreditMemos", CreditMemoAddOrder },
            { "Estimates", EstimateAddOrder },
            { "Deposits", DepositAddOrder },
            { "Checks", CheckAddOrder },
            { "VendorCredits", VendorCreditAddOrder },
            { "InventoryAdjustments", InventoryAdjustmentAddOrder },
            { "Transfers", TransferAddOrder },

            // Add element names (as used in QBXML element tags)
            { "AccountAdd", AccountAddOrder },
            { "CustomerAdd", CustomerAddOrder },
            { "VendorAdd", VendorAddOrder },
            { "EmployeeAdd", EmployeeAddOrder },
            { "ClassAdd", ClassAddOrder },
            { "PaymentMethodAdd", PaymentMethodAddOrder },
            { "StandardTermsAdd", StandardTermsAddOrder },
            { "SalesTaxCodeAdd", SalesTaxCodeAddOrder },
            { "ShipMethodAdd", ShipMethodAddOrder },
            { "CustomerTypeAdd", CustomerTypeAddOrder },
            { "VendorTypeAdd", VendorTypeAddOrder },
            { "JobTypeAdd", JobTypeAddOrder },
            { "PriceLevelAdd", PriceLevelAddOrder },
            { "InvoiceAdd", InvoiceAddOrder },
            { "BillAdd", BillAddOrder },
            { "ReceivePaymentAdd", ReceivePaymentAddOrder },
            { "SalesReceiptAdd", SalesReceiptAddOrder },
            { "PurchaseOrderAdd", PurchaseOrderAddOrder },
            { "JournalEntryAdd", JournalEntryAddOrder },
            { "CreditMemoAdd", CreditMemoAddOrder },
            { "EstimateAdd", EstimateAddOrder },
            { "DepositAdd", DepositAddOrder },
            { "CheckAdd", CheckAddOrder },
            { "VendorCreditAdd", VendorCreditAddOrder },
            { "InventoryAdjustmentAdd", InventoryAdjustmentAddOrder },
            { "TransferAdd", TransferAddOrder },

            // Item subtypes
            { "Items", ItemServiceAddOrder },  // Default fallback for generic "Items"
            { "ItemService", ItemServiceAddOrder },
            { "ItemServiceAdd", ItemServiceAddOrder },
            { "ItemInventory", ItemInventoryAddOrder },
            { "ItemInventoryAdd", ItemInventoryAddOrder },
            { "ItemNonInventory", ItemNonInventoryAddOrder },
            { "ItemNonInventoryAdd", ItemNonInventoryAddOrder },
            { "ItemOtherCharge", ItemOtherChargeAddOrder },
            { "ItemOtherChargeAdd", ItemOtherChargeAddOrder },
            { "ItemDiscount", ItemDiscountAddOrder },
            { "ItemDiscountAdd", ItemDiscountAddOrder },
            { "ItemSalesTax", ItemSalesTaxAddOrder },
            { "ItemSalesTaxAdd", ItemSalesTaxAddOrder },
            { "ItemGroup", ItemGroupAddOrder },
            { "ItemGroupAdd", ItemGroupAddOrder },
        };

        /// <summary>
        /// Gets the field ordering dictionary for a given entity type.
        /// Returns an empty dictionary if the entity type is not recognized
        /// (fields will be output in their original order).
        /// </summary>
        /// <param name="entityType">Entity type name — accepts collection names
        /// (Customers, Vendors), Add element names (CustomerAdd, VendorAdd),
        /// or item subtypes (ItemService, ItemInventory).</param>
        /// <returns>Dictionary mapping field names to ordinal positions.</returns>
        public static Dictionary<string, int> GetFieldOrderMap(string entityType)
        {
            if (string.IsNullOrEmpty(entityType))
                return new Dictionary<string, int>();

            if (_entityOrderMap.TryGetValue(entityType, out var orderMap))
                return orderMap;

            // Try stripping common suffixes/prefixes for fuzzy matching
            var normalized = entityType
                .Replace("Add", "")
                .Replace("Mod", "")
                .Replace("Rq", "")
                .Replace("Ret", "");

            // Try singular forms
            foreach (var key in _entityOrderMap.Keys)
            {
                if (key.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
                    key.Equals(normalized + "s", StringComparison.OrdinalIgnoreCase))
                {
                    return _entityOrderMap[key];
                }
            }

            Log.Debug("No QBXML field ordering map found for entity type '{EntityType}'. " +
                      "Fields will be output in original order.", entityType);
            return new Dictionary<string, int>();
        }

        /// <summary>
        /// Gets the field ordering dictionary for address sub-elements.
        /// Used to sort child elements inside BillAddress, ShipAddress, etc.
        /// </summary>
        public static Dictionary<string, int> GetAddressFieldOrder()
        {
            return AddressFieldOrder;
        }

        /// <summary>
        /// Gets the field ordering dictionary for line item sub-elements
        /// based on the line type name.
        /// </summary>
        /// <param name="lineTypeName">The QBXML line element name (e.g., "InvoiceLineAdd",
        /// "ExpenseLineAdd", "JournalDebitLine").</param>
        public static Dictionary<string, int> GetLineItemFieldOrder(string lineTypeName)
        {
            if (string.IsNullOrEmpty(lineTypeName))
                return new Dictionary<string, int>();

            // Normalize: strip Add/Ret suffixes for matching
            var normalized = lineTypeName.Replace("Add", "").Replace("Ret", "");

            if (normalized.Contains("InvoiceLine", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("SalesReceiptLine", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("CreditMemoLine", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("EstimateLine", StringComparison.OrdinalIgnoreCase))
                return InvoiceLineAddFieldOrder;

            if (normalized.Contains("ExpenseLine", StringComparison.OrdinalIgnoreCase))
                return ExpenseLineAddFieldOrder;

            if (normalized.Contains("ItemLine", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("PurchaseOrderLine", StringComparison.OrdinalIgnoreCase))
                return ItemLineAddFieldOrder;

            if (normalized.Contains("JournalDebit", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("JournalCredit", StringComparison.OrdinalIgnoreCase))
                return JournalLineFieldOrder;

            if (normalized.Contains("DepositLine", StringComparison.OrdinalIgnoreCase))
                return DepositLineAddFieldOrder;

            return new Dictionary<string, int>();
        }

        /// <summary>
        /// Determines if the given element name is an address block that needs
        /// internal field ordering.
        /// </summary>
        public static bool IsAddressElement(string elementName)
        {
            if (string.IsNullOrEmpty(elementName)) return false;
            return elementName.EndsWith("Address", StringComparison.OrdinalIgnoreCase) ||
                   elementName == "BillAddress" ||
                   elementName == "ShipAddress" ||
                   elementName == "VendorAddress" ||
                   elementName == "EmployeeAddress" ||
                   elementName == "ShipToAddress";
        }

        /// <summary>
        /// Returns the sort index for a field within a given ordering map.
        /// Fields not in the map get 9999 (sorted to the end).
        /// </summary>
        public static int GetFieldIndex(Dictionary<string, int> orderMap, string fieldName)
        {
            return orderMap.TryGetValue(fieldName, out var index) ? index : 9999;
        }
    }
}
