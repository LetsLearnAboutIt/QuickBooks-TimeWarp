# Debugging Notes — QB-TimeWarp

## ⚠️ CURRENT STATUS (May 19, 2026 — Late Session)

**Root cause analysis COMPLETE. 5 root causes + 1 bug identified. Fixes NOT yet implemented.**

---

## Root Cause Summary — All 5 Issues

### Root Cause #1: Hierarchical Names (Leaf + ParentRef)

**Problem**: QB uses hierarchical names like `"Automobile:Fuel"` for sub-accounts/sub-items. The import code sends the FULL hierarchical name as the `Name` field, but QB expects:
- `Name` = leaf name only (e.g., `"Fuel"`)
- `ParentRef` = reference to the parent (e.g., `"Automobile"`)

**Impact**: All sub-accounts, sub-customers, sub-items fail with "invalid name" or create wrong records.

**Fix needed**: Parse any name containing `:` — extract leaf as `Name`, look up parent and set `ParentRef`.

---

### Root Cause #2: IsActive Field (All Must Be Active on Import)

**Problem**: Exported data from QB 2023 includes `IsActive=false` for inactive entities. QB 2021 may reject imports of inactive entities, or the import creates them as inactive which breaks subsequent reference lookups.

**Impact**: Entities marked inactive fail to import, or later stages can't reference them.

**Fix needed**: During transformation, set `IsActive=true` on ALL entities regardless of their 2023 status. The accountant will deactivate as needed post-migration.

> **Note**: The config `ReactivateInactiveEntities=true` exists but may not be applied correctly in the staged import path. Verify it's enforced.

---

### Root Cause #3: Account Types Must Exist BEFORE Accounts

**Problem**: When importing accounts, each account has an `AccountType` (e.g., Bank, Expense, Income, COGS). QB 2021 requires the account type to be valid. While QB has built-in account types, some custom types or sub-types from QB 2023 may not exist in QB 2021.

**Impact**: Account imports fail if the account type doesn't map to a valid QB 2021 type.

**Fix needed**: Stage 0 must verify all account types in the exported data map to valid QB 2021 account types. Create any missing ones or map them to equivalent types.

---

### Root Cause #4: Missing Reference Types (Sales Tax, Terms, Customer Types, etc.)

**Problem**: Entities reference foundation items (SalesTaxCodes, Terms, PaymentMethods, CustomerTypes, VendorTypes) that may not exist in QB 2021. The staged import's auto-creation partially addresses this, but:
- Auto-creation may fail silently
- Some reference types need specific properties (tax rates, term days)
- The creation must happen BEFORE any entity that references them

**Impact**: StatusCode 3140 ("referenced item does not exist") on entity imports.

**Fix needed**: Stage 0 must explicitly:
1. Query QB 2021 for existing reference types
2. Compare against all referenced types in exported data
3. Create missing ones with appropriate defaults
4. Verify creation succeeded before proceeding

---

### Root Cause #5: Field Compatibility (QB 2023 SDK 16.0 → QB 2021 SDK 15.0)

**Problem**: QB 2023 uses SDK 16.0 with fields that don't exist in SDK 15.0. Sending these fields causes `0x80040400` errors.

**Impact**: Records with SDK 16.0-only fields are rejected entirely.

**Fix needed**: The transformer must strip all SDK 16.0-only fields before import. The `QBSDKVersionHelper.cs` has lists of incompatible fields, but:
- The list may be incomplete
- New fields discovered during import must be dynamically excluded
- Log which fields were stripped so we can verify nothing critical was lost

---

### Bug #6: Success Detection — 359 Phantom Successes

**Problem**: The import reported 359 successes, but examination showed these were false positives. The success detection logic may be counting records that were "processed" but not actually imported, or counting the request submission rather than the QB response.

**Impact**: Misleading progress reports. Can't trust success counts.

**Fix needed**: Verify success detection checks the actual QB response `statusCode="0"`, not just the absence of an exception.

---

## Correct Order of Operations

```
┌─────────────────────────────────────────────────────────────┐
│ TRANSFORMATION TIMING:                                       │
│   1. Export from QB 2023                                     │
│   2. CLOSE QB 2023                                           │
│   3. OPEN QB 2021                                            │
│   4. Transform (2023 format → 2021 format)                   │
│   5. Import into QB 2021                                     │
└─────────────────────────────────────────────────────────────┘

Stage 0 — Pre-Import Prerequisites
  ├── Verify/create account types (map 2023 types → 2021 types)
  ├── Create missing SalesTaxCodes
  ├── Create missing Terms (payment terms)
  ├── Create missing PaymentMethods
  ├── Create missing CustomerTypes, VendorTypes, JobTypes
  ├── Create missing ShipMethods
  ├── Create missing Classes (with hierarchy)
  └── VALIDATE all prerequisites exist before continuing

Stage 1 — Foundation: Accounts
  ├── Parse hierarchical names (leaf + ParentRef)
  ├── Set IsActive=true on all
  ├── Strip SDK 16.0-only fields
  ├── Import parent accounts FIRST, then sub-accounts
  └── Store ListID mappings for reference resolution

Stage 2 — Entities: Customers, Vendors, Employees
  ├── Parse hierarchical names (leaf + ParentRef)
  ├── Set IsActive=true on all
  ├── Strip SDK 16.0-only fields
  ├── Resolve references (Terms, SalesTaxCode, etc.) by ListID
  └── Store ListID mappings

Stage 3 — Items: All subtypes
  ├── Parse hierarchical names (leaf + ParentRef for sub-items)
  ├── Set IsActive=true on all
  ├── Strip SDK 16.0-only fields
  ├── Resolve account references by ListID
  └── Store ListID mappings

Stage 4 — Transactions: Invoices, Bills, etc.
  ├── Strip SDK 16.0-only fields
  ├── Resolve ALL references (Customer, Vendor, Item, Account) by ListID
  ├── Validate date formats
  └── Import in chronological order where possible
```

---

## Fix History (Chronological)

### Fix #1: QBXML Element Ordering (0x80040400 Root Cause)

**Date:** 2026-05-19  
**Status:** ✅ Applied  
**Severity:** CRITICAL — This was the PRIMARY cause of 100% import failure.

#### Problem

All records imported into QB 2021 were rejected with HRESULT `0x80040400`:
> "QuickBooks found an error when parsing the provided XML text stream."

**Root Cause:** The `BuildFieldsXml` method in `DataImporter.cs` iterated over JSON properties in their natural (insertion/arbitrary) order. However, QBXML uses `xs:sequence` in its XSD schema, meaning **elements MUST appear in the exact order defined by the schema**. Random field ordering caused the QB SDK XML parser to reject every single request.

#### Fix Applied

1. **Created `Helpers/QBXMLFieldOrdering.cs`** — Comprehensive field ordering helper defining XSD-correct element sequence for every entity type (list entities, item subtypes, transactions, sub-elements).

2. **Updated `DataImporter.cs`** — `BuildFieldsXml`, `BuildAddRequestXml`, `BuildItemAddRequest`, `BuildLineItemsXml` all sort fields by XSD ordinal index before generating XML.

3. **Ordering logic:** Fields in XSD map sorted by ordinal index. Unknown fields get index 9999 (appended at end).

#### Files Modified
- `Helpers/QBXMLFieldOrdering.cs` (NEW)
- `Services/DataImporter.cs`

---

### Fix #2: Staged Import with Dependency Analysis (StatusCode 3140 Root Cause)

**Date:** 2026-05-19  
**Status:** ✅ Applied  
**Severity:** HIGH — Resolves import failures from missing referenced items.

#### Problem

When importing entities, QB 2021 returns **StatusCode 3140**:
> "The referenced item (SalesTaxCode, Terms, etc.) does not exist."

**Root Cause:** Flat import didn't verify referenced items exist before importing referencing entities.

#### Fix Applied

1. **Created `Helpers/DependencyAnalyzer.cs`** — Pre-import dependency scanner
2. **Defined 4 Import Stages** in `DataImporter.cs`:
   - Stage 1 (Foundation): Accounts, SalesTaxCodes, Terms, PaymentMethods, etc.
   - Stage 2 (Entities): Customers, Vendors, Employees
   - Stage 3 (Items): All item subtypes
   - Stage 4 (Transactions): All transaction types
3. **Added auto-creation of missing dependencies**
4. **Added reference resolution** (FullName → ListID mapping)
5. **Added stage validation** with halt-on-failure for critical stages

#### Files Modified
- `Helpers/DependencyAnalyzer.cs` (NEW)
- `Services/DataImporter.cs`
- `Models/MigrationModels.cs`
- `Models/ConfigurationModels.cs`
- `appsettings.json`

---

## Remaining Fixes Needed

See **CRITICAL_FIXES_NEEDED.MD** for the complete implementation plan with priorities and dependencies.
