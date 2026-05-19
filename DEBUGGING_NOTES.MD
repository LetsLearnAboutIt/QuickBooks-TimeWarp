# Debugging Notes — QB-TimeWarp

## Fix History

### Fix #1: QBXML Element Ordering (0x80040400 Root Cause)

**Date:** 2026-05-19  
**Status:** ✅ Applied  
**Severity:** CRITICAL — This was the PRIMARY cause of 100% import failure.

#### Problem

All records imported into QB 2021 were rejected with HRESULT `0x80040400`:
> "QuickBooks found an error when parsing the provided XML text stream."

**Root Cause:** The `BuildFieldsXml` method in `DataImporter.cs` iterated over JSON properties in their natural (insertion/arbitrary) order. However, QBXML uses `xs:sequence` in its XSD schema, meaning **elements MUST appear in the exact order defined by the schema**. Random field ordering caused the QB SDK XML parser to reject every single request.

#### Fix Applied

1. **Created `Helpers/QBXMLFieldOrdering.cs`** — A comprehensive field ordering helper that defines XSD-correct element sequence for every entity type:
   - **List entities:** Customer, Vendor, Account, Employee, Class, Terms, PaymentMethod, SalesTaxCode, ShipMethod, CustomerType, VendorType, JobType, PriceLevel
   - **Item subtypes:** ItemService, ItemInventory, ItemNonInventory, ItemOtherCharge, ItemDiscount, ItemSalesTax, ItemGroup
   - **Transactions:** Invoice, Bill, Check, Deposit, JournalEntry, ReceivePayment, SalesReceipt, CreditMemo, Estimate, PurchaseOrder, VendorCredit, InventoryAdjustment, Transfer
   - **Sub-element ordering:** Address blocks (Addr1→Country), InvoiceLineAdd, ExpenseLineAdd, ItemLineAdd, JournalDebitLine/CreditLine, DepositLineAdd

2. **Updated `DataImporter.cs`** — Three methods modified:
   - `BuildFieldsXml(JObject fields, string entityType)` — Now accepts entity type, sorts all top-level fields by XSD index before generating XML. Address sub-elements also sorted.
   - `BuildAddRequestXml` — Passes entity type to `BuildFieldsXml`.
   - `BuildItemAddRequest` — Passes item subtype to `BuildFieldsXml`.
   - `BuildLineItemsXml` — Line item properties now sorted by their line-type-specific XSD ordering.

3. **Ordering logic:**
   - Fields in the XSD map are sorted by their ordinal index (lower = earlier in XML)
   - Fields NOT in the map get index 9999 (appended at end, alphabetically)
   - This is safe: unknown fields at the end may still be rejected by QB, but known fields will be in correct order
   - Debug logging shows the field order applied for each entity

#### Sources for Element Order

- **Intuit QBXML XSD schema** (`qbxmlops20.xml`) from [qbwc/qbxml GitHub](https://github.com/qbwc/qbxml/blob/master/schema/qbxmlops20.xml)
- **consolibyte/quickbooks-php** schema definitions (`_reorderPathsPaths` methods)
- **Intuit Developer documentation** and Stack Overflow empirical reports

#### Files Modified

| File | Change |
|------|--------|
| `Helpers/QBXMLFieldOrdering.cs` | **NEW** — Field ordering maps for all entity types |
| `Services/DataImporter.cs` | Updated `BuildFieldsXml`, `BuildAddRequestXml`, `BuildItemAddRequest`, `BuildLineItemsXml` |
| `DEBUGGING_NOTES.MD` | **NEW** — This file |

#### How to Verify

After deploying, check the logs for:
```
QBXML field ordering applied for 'Customers': [Name, IsActive, CompanyName, FirstName, LastName, BillAddress, ...]
```

If 0x80040400 errors persist for a specific entity type:
1. Check if its fields are in the ordering map
2. Compare generated XML against the QBXML XSD schema
3. Add missing fields to the appropriate dictionary in `QBXMLFieldOrdering.cs`

#### Key Insight

The QBXML SDK's error message ("error when parsing the provided XML text stream") is **intentionally vague** — it does not tell you which element is out of order or what order was expected. The only way to fix this is to match the XSD sequence exactly. The QBXML validator tool (included with the SDK) is reportedly more lenient than QB Desktop itself, so passing validation does NOT guarantee QB will accept the request.
