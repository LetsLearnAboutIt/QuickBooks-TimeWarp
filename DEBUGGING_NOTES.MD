# Debugging Notes — QB-TimeWarp

## Fix History

### Fix #2: Staged Import with Dependency Analysis (StatusCode 3140 Root Cause)

**Date:** 2026-05-19  
**Status:** ✅ Applied  
**Severity:** HIGH — Resolves import failures from missing referenced items.

#### Problem

When importing entities (especially Customers), QB 2021 returns **StatusCode 3140** errors:
> "The referenced item (SalesTaxCode, Terms, etc.) does not exist."

**Root Cause:** The flat `ImportAll()` method imports entities in a fixed order defined by `ImportOrder`, but does NOT verify that items referenced by entities actually exist in QB 2021 before importing. For example:
- A Customer references `SalesTaxCodeRef: "Tax"` — but the sales tax code "Tax" hasn't been imported yet (or doesn't exist at all in the exported data).
- A Vendor references `TermsRef: "Net 30"` — but Terms haven't been created in QB 2021 yet.

This is a **dependency ordering** problem: entities reference foundation items that must exist first.

#### Fix Applied — Staged Import Architecture

1. **Created `Helpers/DependencyAnalyzer.cs`** — Pre-import dependency scanner:
   - Scans ALL exported JSON entities and their fields/line items
   - Identifies every reference field (`SalesTaxCodeRef`, `TermsRef`, `ClassRef`, `AccountRef`, etc.)
   - Extracts referenced FullNames and groups them by type
   - Cross-references against exported data to identify **missing items** — referenced but not present
   - Returns a `DependencyAnalysisResult` with complete dependency graph

2. **Defined 4 Import Stages** (in `DataImporter.cs`):
   ```
   Stage 1 (Foundation): Accounts, SalesTaxCodes, Terms, PaymentMethods,
                          CustomerTypes, VendorTypes, JobTypes, Classes,
                          ShipMethods, PriceLevels
   
   Stage 2 (Entities):   Customers, Vendors, Employees
   
   Stage 3 (Items):      All Item subtypes (Service, Inventory, NonInventory, Groups)
   
   Stage 4 (Transactions): Invoices, Bills, Payments, SalesReceipts, JournalEntries,
                            CreditMemos, Estimates, Deposits, Checks, VendorCredits,
                            InventoryAdjustments, Transfers
   ```

3. **Added `ImportDataInStages()` method** — New primary import entry point:
   - Phase 0: Run `DependencyAnalyzer` to scan all data and build dependency graph
   - Auto-creates missing foundation items (SalesTaxCodes, Terms, PaymentMethods, etc.)
   - Executes stages sequentially with validation between each
   - Resolves references (adds ListIDs) from earlier stages before importing later stages
   - Halts if critical stage fails completely (configurable)

4. **Added Reference Resolution**:
   - As items are imported, their new QB 2021 ListIDs are stored in `_nameToListIdMap`
   - Before importing Stage 2+ entities, `ResolveReferences()` walks all Ref fields
   - Looks up the FullName → ListID mapping and inserts the ListID into the ref object
   - This allows QB 2021 to resolve references by ListID (more reliable than FullName)

5. **Added Auto-Creation of Missing Dependencies**:
   - If dependency analysis finds items referenced but not in exported data:
     - SalesTaxCodes → creates with `IsTaxable=true`
     - Terms → creates as `StandardTerms` with 30-day default
     - PaymentMethods → creates with name
     - CustomerTypes, VendorTypes, JobTypes, ShipMethods → creates with name
     - Classes → creates with hierarchy support (parent:child)
   - All auto-created items tracked in `StageSummary.AutoCreatedItemNames`

6. **Added Stage Validation**:
   - After each stage, checks if it passed (had some successes)
   - Critical stages (1-3) halt the import if ALL records fail
   - Non-critical stages (4 - Transactions) continue with warnings
   - Configurable via `ValidateBetweenStages` and `HaltOnFoundationFailure`

#### New Configuration Options (`appsettings.json`)

```json
{
  "Import": {
    "UseStagedImport": true,
    "AutoCreateMissingDependencies": true,
    "ValidateBetweenStages": true,
    "HaltOnFoundationFailure": true,
    "Stages": {
      "Stage1_Foundation": true,
      "Stage2_Entities": true,
      "Stage3_Items": true,
      "Stage4_Transactions": true
    }
  }
}
```

Set `UseStagedImport: false` to fall back to the legacy flat `ImportAll()` approach.

#### Files Modified

| File | Change |
|------|--------|
| `Helpers/DependencyAnalyzer.cs` | **NEW** — Pre-import dependency scanner |
| `Services/DataImporter.cs` | Added `ImportDataInStages()`, `AnalyzeDependencies()`, reference resolution, auto-creation, stage execution |
| `Models/MigrationModels.cs` | Added `ImportStage`, `StageSummary`, `StagedImportSummary` models |
| `Models/ConfigurationModels.cs` | Added `StagedImportStagesConfig`, new `ImportConfig` properties |
| `appsettings.json` | Added staged import configuration |
| `DEBUGGING_NOTES.MD` | This entry |

#### How to Verify

Check logs for the staged import flow:
```
╔═══════════════════════════════════════════════════════╗
║  STAGED IMPORT — Dependency-Aware Multi-Stage Import  ║
╚═══════════════════════════════════════════════════════╝
  Phase 0: Pre-Import Dependency Analysis
  ...
  Stage 1/4: Foundation
  [1/10] Importing Accounts (45 records)...
  ...
  Stage 2/4: Entities
  [1/3] Importing Customers (120 records)...
```

If 3140 errors STILL occur:
1. Check the dependency analysis log — is the referenced item listed?
2. Check if auto-creation succeeded for that item type
3. Verify the item exists in QB 2021 (may need manual creation)
4. Check if the reference FullName matches exactly (case-sensitive in some QB operations)

#### Key Insight

QB 2021 StatusCode 3140 ("The specified item does not exist") is almost always a **dependency ordering** problem. The item being referenced MUST exist in the target company file before any entity that references it can be imported. The staged import ensures this by design.

---

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
