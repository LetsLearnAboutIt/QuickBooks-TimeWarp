namespace QB_TimeWarp.Models
{
    /// <summary>
    /// All QuickBooks entity types supported by the migration tool.
    /// </summary>
    public enum QBEntityType
    {
        // ---- Chart of Accounts & Reference Lists (import first) ----
        Accounts,
        PaymentMethods,
        Terms,
        Classes,
        SalesTaxCodes,
        ShipMethods,
        CustomerTypes,
        VendorTypes,
        JobTypes,
        PriceLevels,

        // ---- Master Lists ----
        Customers,
        Vendors,
        Employees,
        Items,

        // ---- Transactions ----
        Invoices,
        Bills,
        Payments,
        SalesReceipts,
        PurchaseOrders,
        JournalEntries,
        CreditMemos,
        Estimates,
        Deposits,
        Checks,
        VendorCredits,
        InventoryAdjustments,
        Transfers,

        // ---- Settings ----
        Preferences,
        CompanyInfo
    }

    /// <summary>
    /// Tracks the status of each migration operation.
    /// </summary>
    public enum MigrationStatus
    {
        Pending,
        InProgress,
        Completed,
        CompletedWithErrors,
        Failed,
        Skipped
    }

    /// <summary>
    /// Field mapping action types.
    /// </summary>
    public enum FieldMappingAction
    {
        Map,
        Transform,
        Default,
        Skip
    }
}
