# QB-TimeWarp — QuickBooks 2023 → 2021 Data Migration Tool

A production-ready C# console application that migrates all data from a QuickBooks Desktop 2023 company file to a QuickBooks Desktop 2021 company file using the QBXML SDK.

**Repository**: https://github.com/LetsLearnAboutIt/QuickBooks-TimeWarp

---

## Features

- **Full Data Export** — Exports all entities from QB 2023: Customers, Vendors, Accounts, Items, Employees, and all transaction types (Invoices, Bills, Payments, Journal Entries, Sales Receipts, Purchase Orders, Credit Memos, Estimates, Deposits, Checks, Vendor Credits, Inventory Adjustments, Transfers)
- **Inactive Entity Support** — Exports both active AND inactive records with `ActiveStatus=All`, with separate active/inactive counts in export summaries
- **Inactive → Active Reactivation** — Automatically converts all inactive entities (Customers, Vendors, Items, Accounts, Employees) to active during transformation, with detailed logging and reporting
- **Class Tracking Preservation** — Preserves class assignments on all transaction types (Invoices, Bills, Journal Entries, Sales Receipts, Purchase Orders) at both header and line item levels; automatically creates missing classes in QB 2021
- **Accounting Model Matching** — Detects and preserves the accounting method (Cash basis vs Accrual basis) from QB 2023 and applies it to QB 2021 via QBXML PreferencesQuery/PreferencesMod
- **Schema Extraction** — Documents QB 2021's supported fields, data types, and constraints as JSON
- **Configurable Field Mapping** — Human-editable `FieldMappings.json` maps QB 2023 fields to QB 2021 equivalents with support for renaming, transformations, defaults, and skipping deprecated fields
- **Dependency-Ordered Import** — Imports lists before transactions, respecting referential integrity (Accounts → Classes → Customers/Vendors → Items → Invoices/Bills → Payments)
- **Journal Integrity Validation** — Comprehensive journal entry validation ensures total debits equal total credits for every journal entry, validates invoice line item sums, bill amounts, and payment application balances
- **Comprehensive Validation** — Field-by-field comparison, entity count verification, financial totals reconciliation, reactivation verification, class tracking validation, and accounting model validation
- **Pre-Import Validation** — Catches journal imbalances and financial integrity issues before importing to QB 2021
- **Error Recovery** — Skips problematic records and continues; logs detailed error reasons for every failure
- **Rich Console Output** — Step-by-step progress indicators, ASCII art banner, per-entity status breakdown
- **Structured Logging** — Serilog-based logging to both console and timestamped log files with reactivation summaries, class tracking reports, and journal validation results

---

## Prerequisites

### Software Requirements

| Software | Version | Notes |
|----------|---------|-------|
| Windows | 10/11 | x64 or x86 |
| .NET SDK | 6.0+ | [Download](https://dotnet.microsoft.com/download/dotnet/6.0) |
| QuickBooks Desktop | 2023 **and** 2021 | Both must be installed |
| QuickBooks SDK | 16.0 (for QB 2023) | [Intuit Developer](https://developer.intuit.com/app/developer/qbdesktop/docs/get-started) |
| Visual Studio | 2022 (recommended) | Or VS Code + .NET CLI |

### QuickBooks SDK Installation

1. Download the QuickBooks Desktop SDK from [Intuit Developer](https://developer.intuit.com/app/developer/qbdesktop/docs/get-started)
2. Run the installer — this registers the `QBXMLRP2.RequestProcessor` COM object
3. Verify installation:
   ```powershell
   # Check COM registration
   reg query "HKCR\QBXMLRP2.RequestProcessor" 2>$null
   # Should return a valid registry key
   ```

### QuickBooks Authorization

**Both** QB 2023 and QB 2021 instances must authorize this application:

1. Open QuickBooks Desktop (2023 or 2021)
2. Open the company file you'll be using
3. Go to **Edit → Preferences → Integrated Applications → Company Preferences**
4. When QB-TimeWarp first connects, a dialog will appear asking to grant access
5. Select **"Yes, always allow access even if QuickBooks is not running"**
6. Click **OK** / **Continue**

> **Important**: You must be logged in as the QuickBooks Admin user to authorize applications.

---

## Project Structure

```
QB-TimeWarp/
├── Program.cs                          # Main orchestrator (entry point)
├── QB-TimeWarp.csproj                  # Project file with NuGet references
├── QB-TimeWarp.sln                     # Solution file
├── appsettings.json                    # Application configuration
├── README.md                           # This file
│
├── Configuration/
│   └── FieldMappings.json              # Field transformation rules (editable)
│
├── Models/
│   ├── QBEntityType.cs                 # Entity type enums and status enums
│   ├── MigrationModels.cs              # Core data models (QBEntity, CompanyPreferences, ClassTracking, etc.)
│   ├── ValidationModels.cs             # Validation report models (incl. reactivation, class, accounting)
│   ├── ConfigurationModels.cs          # Configuration binding models (incl. TransformationRules)
│   └── FieldMappingModels.cs           # Field mapping configuration models
│
├── Services/
│   ├── QBConnectionManager.cs          # QBXML SDK connection & session management
│   ├── SchemaExtractor.cs              # QB 2021 schema discovery
│   ├── DataExporter.cs                 # QB 2023 data export (active + inactive)
│   ├── DataTransformer.cs              # Data transformation engine (reactivation, class, accounting)
│   ├── DataImporter.cs                 # QB 2021 data import (class creation, preferences)
│   └── DataValidator.cs                # Post-import validation & reconciliation
│
├── Helpers/
│   ├── TransformFunctions.cs           # Built-in transformation functions
│   └── ProgressReporter.cs             # Console progress indicators
│
├── ExportedData/                       # (created at runtime) Exported JSON files
├── Schemas/                            # (created at runtime) Schema JSON files
├── Validation/                         # (created at runtime) Validation reports
└── Logs/                               # (created at runtime) Log files
```

---

## Setup & Configuration

### 1. Clone and Build

```bash
# Clone the project
cd QB-TimeWarp

# Restore NuGet packages
dotnet restore

# Build (must target x86 for COM interop with QuickBooks)
dotnet build -c Release
```

### 2. Add QuickBooks SDK COM References

**Option A — Visual Studio:**
1. Open `QB-TimeWarp.sln` in Visual Studio 2022
2. Right-click **References** → **Add Reference** → **COM**
3. Search for and add: `QBFC16Lib` (QB 2023 SDK)
4. Build the project

**Option B — Manual Interop Assembly:**
```powershell
# Generate interop assemblies from the SDK type libraries
mkdir lib
tlbimp "C:\Program Files (x86)\Intuit\IDN\QBSDK16.0\tools\QBFC16\QBFC16Lib.dll" /out:lib\Interop.QBFC16.dll

# Then uncomment the Reference lines in QB-TimeWarp.csproj
```

> **Note**: The project uses late-bound COM interop (`dynamic` + `Activator.CreateInstance`) via `QBXMLRP2.RequestProcessor`, so explicit COM references are only needed if you want compile-time type safety.

### 3. Configure `appsettings.json`

Edit the following settings:

```json
{
  "QuickBooks": {
    "QB2023": {
      "CompanyFilePath": "C:\\path\\to\\your\\Company2023.QBW"
    },
    "QB2021": {
      "CompanyFilePath": "C:\\path\\to\\your\\BlankCompany2021.QBW"
    }
  }
}
```

#### Key Settings:

| Setting | Description |
|---------|-------------|
| `QuickBooks.QB2023.CompanyFilePath` | Path to your source QB 2023 `.QBW` file |
| `QuickBooks.QB2021.CompanyFilePath` | Path to your target QB 2021 `.QBW` file (should be blank/new) |
| `Export.BatchSize` | Number of records per query batch (default: 500) |
| `Export.IncludeInactiveRecords` | Whether to export inactive list items (default: true) |
| `Export.DateRangeStart/End` | Optional date filter for transactions (empty = all) |
| `Import.SkipOnError` | Skip failed records and continue (default: true) |
| `Import.DryRun` | Validate without actually importing (default: false) |
| `Validation.ToleranceAmount` | Acceptable rounding difference for financial comparisons (default: 0.01) |
| `Validation.EnableJournalValidation` | Enable journal debit/credit balance validation (default: true) |
| `TransformationRules.ReactivateInactiveEntities` | Convert all inactive entities to active (default: true) |
| `TransformationRules.PreserveClassTracking` | Preserve class assignments on transactions (default: true) |
| `TransformationRules.MatchAccountingModel` | Match Cash/Accrual accounting method (default: true) |

### 4. Customize `FieldMappings.json`

The field mappings file controls how every field is translated from QB 2023 to QB 2021 format. Review and customize it for your specific data:

```json
{
  "entityMappings": {
    "Customer": {
      "fieldMappings": [
        {
          "sourceField": "Name",
          "targetField": "Name",
          "action": "map",
          "maxLength": 41,
          "notes": "QB 2021 Name max length is 41 characters"
        },
        {
          "sourceField": "ClassRef.FullName",
          "targetField": "ClassRef.FullName",
          "action": "map",
          "notes": "Class tracking - preserves class assignment"
        }
      ]
    }
  }
}
```

#### Mapping Actions:

| Action | Description |
|--------|-------------|
| `map` | Direct field copy (with optional `maxLength` truncation) |
| `transform` | Apply a named transformation function |
| `default` | Use a default value if source field is missing |
| `skip` | Exclude this field from the import |

---

## Transformation Rules

QB-TimeWarp applies three critical transformation rules during migration. Each can be independently enabled/disabled via `appsettings.json`.

### 1. Inactive → Active Reactivation

**Config**: `TransformationRules.ReactivateInactiveEntities` (default: `true`)

During transformation, all inactive entities from QB 2023 are converted to active for QB 2021:

| Entity Type | Behavior |
|-------------|----------|
| **Customers** | Inactive → Active (IsActive = true) |
| **Vendors** | Inactive → Active (IsActive = true) |
| **Items** | Inactive → Active (IsActive = true) |
| **Accounts** | Inactive → Active (IsActive = true) |
| **Employees** | Inactive → Active (IsActive = true) |

**How it works:**
1. During export, both active and inactive entities are captured with `ActiveStatus=All`
2. During transformation, `DataTransformer` checks each entity's `IsActive` status
3. If inactive, the entity's `IsActive` is set to `true` and the `IsActive` field in `Fields` is updated
4. A detailed log tracks every reactivated entity by type and name
5. The transformation report includes counts per entity type

**Example log output:**
```
  REACTIVATED ENTITIES SUMMARY
  ────────────────────────────────────────────
    Accounts: 3 entities reactivated
    Customers: 8 entities reactivated
    Items: 12 entities reactivated
    Vendors: 2 entities reactivated
    ────────────────────────────────
    TOTAL: 25 entities reactivated (inactive → active)
```

**Validation:** After import, the validator checks that every entity that was inactive in QB 2023 is now active in QB 2021.

### 2. Class Tracking Preservation

**Config**: `TransformationRules.PreserveClassTracking` (default: `true`)

Class assignments are preserved on all supported transaction types at both header and line item levels:

| Transaction Type | Header Class | Line Item Class |
|-----------------|-------------|-----------------|
| **Invoices** | `ClassRef.FullName` | `InvoiceLineAdd.ClassRef.FullName` |
| **Bills** | `ClassRef.FullName` | `ExpenseLineAdd.ClassRef.FullName` |
| **Journal Entries** | — | `JournalDebitLine.ClassRef.FullName`, `JournalCreditLine.ClassRef.FullName` |
| **Sales Receipts** | `ClassRef.FullName` | `SalesReceiptLineAdd.ClassRef.FullName` |
| **Purchase Orders** | `ClassRef.FullName` | `PurchaseOrderLineAdd.ClassRef.FullName` |

**How it works:**
1. During transformation, all class references are discovered from source data
2. The `DataTransformer` preserves `ClassRef.FullName` at header and line item levels
3. Before importing transactions, `DataImporter.EnsureClassesExist()` queries QB 2021 for existing classes
4. Missing classes are automatically created via QBXML `ClassAddRq` (including hierarchical parent classes)
5. Class usage statistics are tracked per transaction type and class name

**Example class tracking summary:**
```
  CLASS TRACKING SUMMARY
  ────────────────────────────────────────────
    Total unique classes: 5
    Class 'Administration': used in 45 transactions
    Class 'Marketing': used in 120 transactions
    Class 'Operations': used in 89 transactions
    Class 'Sales': used in 200 transactions
    Class 'Sales:West Coast': used in 55 transactions
    Class usage by transaction type:
      Invoices: 180 assignments
      Bills: 95 assignments
      JournalEntries: 80 assignments
      SalesReceipts: 54 assignments
      PurchaseOrders: 45 assignments
```

**Validation:** After import, the validator verifies class assignments are preserved by comparing source and target transaction class references. Warnings are generated if class tracking is used in QB 2023 but not enabled in QB 2021.

### 3. Accounting Model Matching (Cash vs Accrual)

**Config**: `TransformationRules.MatchAccountingModel` (default: `true`)

The accounting method from QB 2023 is detected and applied to QB 2021:

**How it works:**
1. Before import, `DataImporter.QueryCompanyPreferences()` queries QB 2023's preferences via `PreferencesQueryRq`
2. The accounting method (Cash or Accrual) and `ReportBasis` are extracted
3. `DataImporter.ApplyAccountingPreferences()` sends a `PreferencesModRq` to QB 2021 to set the matching method
4. After applying, the target preferences are re-queried to verify the change
5. If the SDK cannot modify preferences, a manual instruction is logged

**Company Preferences captured:**
- Accounting Method (Cash / Accrual)
- Report Basis (Cash / Accrual)
- Class Tracking enabled status
- Multi-Currency enabled status
- Fiscal Year Start Month

**Example migration summary:**
```
  Accounting Model: Accrual (matched: YES)
```

**Validation:** After import, the validator compares source and target accounting methods and report bases. A mismatch generates a warning with instructions for manual correction.

---

## Usage

### Full Migration (Default)

```powershell
# Run the complete pipeline: Schema → Export → Transform → Import → Validate
dotnet run
# or
.\QB-TimeWarp.exe
```

### Individual Steps

```powershell
# Export data from QB 2023 only
dotnet run -- --export

# Import previously exported data into QB 2021
dotnet run -- --import

# Run validation only (compare QB 2023 vs QB 2021)
dotnet run -- --validate

# Extract QB 2021 schema only
dotnet run -- --schema

# Show help
dotnet run -- --help
```

### Dry Run (Preview Without Importing)

Set `"DryRun": true` in `appsettings.json` under `Import` to validate the import without sending data to QuickBooks.

---

## Output Files

After a migration run, you'll find:

| Directory | Contents |
|-----------|----------|
| `ExportedData/` | One JSON file per entity type + `_ExportManifest.json` |
| `ExportedData/Transformed/` | Transformed data ready for import |
| `Schemas/` | `QB_Schema_QB_2021.json` — complete field reference |
| `Validation/` | `ValidationReport_*.json` — detailed comparison report |
| `Validation/` | `MigrationReport_*.json` — overall migration summary (includes TransformationReport) |
| `Logs/` | Timestamped log files with full operation details |

---

## Migration Summary Report

The final migration report includes these sections:

| Section | Contents |
|---------|----------|
| **Status** | Overall migration status (Completed / CompletedWithErrors / Failed) |
| **Records** | Total attempted, succeeded, failed |
| **Reactivated Entities** | Count of inactive→active conversions per entity type |
| **Class Tracking** | Classes discovered, created, usage statistics |
| **Accounting Model** | Source method, target method, match status |
| **Validation** | Field discrepancies, financial reconciliation, journal integrity |
| **Journal Integrity** | Balanced/Imbalanced status with mismatch details |

---

## Common QB 2023 vs 2021 Differences

| Difference | How QB-TimeWarp Handles It |
|-----------|---------------------------|
| QB 2023 longer field lengths (Name: 209 chars) | Auto-truncates to QB 2021 limits per `maxLength` in mappings |
| New payment method types (Venmo, Zelle, etc.) | Maps to "Other" via `MapPaymentMethod` transform |
| New job statuses (InNegotiation, OnHold) | Maps to closest QB 2021 equivalent via `MapJobStatus` |
| Enhanced inventory fields | Skips unsupported fields, logs warnings |
| Timezone-aware datetime fields | Strips timezone via `StripTimezone` transform |
| System-generated fields (ListID, TxnID) | Auto-skipped; QB 2021 assigns new IDs |
| Computed fields (Balance, Subtotal) | Auto-skipped; QB 2021 computes from transactions |
| Inactive entities in QB 2023 | Reactivated to active in QB 2021 (configurable) |
| Class tracking differences | Classes auto-created in QB 2021; assignments preserved |
| Accounting method differences | Cash/Accrual basis matched between QB 2023 → 2021 |

---

## Inactive Entity Handling

QB-TimeWarp exports both **active and inactive** records for all list entity types. During transformation, inactive entities are reactivated.

### How It Works

1. **Export**: When `IncludeInactiveRecords` is `true` (default), QBXML queries include `<ActiveStatus>All</ActiveStatus>` for Customers, Vendors, Items, Accounts, Employees, and supporting lists.

2. **Tracking**: Each exported entity has an `IsActive` field. The export manifest reports separate active/inactive counts per entity type.

3. **Reactivation**: When `TransformationRules.ReactivateInactiveEntities` is `true` (default), the `DataTransformer` sets `IsActive = true` for all inactive entities during transformation. Every reactivated entity is logged individually.

4. **Import**: All entities are imported as active into QB 2021.

5. **Validation**: The validator verifies that every previously-inactive entity is now active in QB 2021.

### Export Summary Example

```
  INACTIVE ENTITY SUMMARY
  ────────────────────────────────────────────
    Customers: 5 inactive out of 150 total
    Items: 12 inactive out of 200 total
    Vendors: 3 inactive out of 50 total
    ─────────────────────────────────────
    Total inactive records: 20
    All reactivated during transformation ✓
```

---

## Journal Integrity Validation

The accountant-critical journal validation ensures financial data integrity during migration.

### What Gets Validated

| Check | Description |
|-------|-------------|
| **Journal Entry Balance** | For every journal entry, verifies total debits = total credits |
| **Invoice Line Items** | Verifies invoice line item amounts sum to the stated subtotal/total |
| **Bill Amounts** | Verifies bill line items (expense + item lines) sum to the bill amount |
| **Payment Applications** | Verifies payment applied amounts match the total minus unused amount |

### Pre-Import vs Post-Import Validation

- **Pre-Import**: Runs after data transformation but BEFORE importing to QB 2021. Catches data issues early.
- **Post-Import**: Runs as part of the full validation suite after import. Verifies imported data maintains integrity.

### Severity Levels

| Severity | Criteria | Action Required |
|----------|----------|----------------|
| **Info** | Variance within tolerance (≤ $0.01) | None — rounding difference |
| **Warning** | Small variance ($0.01 – $1.00) | Review — may be data entry issue |
| **Critical** | Large variance (> $1.00) | Must investigate before proceeding |

---

## Validation Checks

QB-TimeWarp performs comprehensive post-import validation:

| Validation | Description |
|------------|-------------|
| **Entity Count Verification** | Source vs target entity counts match |
| **Field-by-Field Comparison** | Every field value compared with tolerance |
| **Financial Reconciliation** | Account balances, AR/AP, transaction sums |
| **Journal Integrity** | Debit/credit balance, invoice/bill/payment integrity |
| **Reactivation Verification** | All formerly-inactive entities confirmed active in QB 2021 |
| **Class Tracking Validation** | Class assignments preserved on all transactions |
| **Accounting Model Validation** | Cash/Accrual method matches between source and target |

---

## Repository Setup

### Cloning the Repository

```bash
git clone https://github.com/LetsLearnAboutIt/QuickBooks-TimeWarp.git
cd QuickBooks-TimeWarp
```

### Building

```bash
dotnet restore
dotnet build -c Release
```

### Development Setup

1. Clone the repo (see above)
2. Open `QB-TimeWarp.sln` in Visual Studio 2022
3. Restore NuGet packages
4. Configure `appsettings.json` with your QB company file paths
5. Build targeting x86 (required for COM interop)
6. Ensure QuickBooks Desktop is running with the company file open

---

## Troubleshooting

### "QBXMLRP2.RequestProcessor not found"
- Install the QuickBooks SDK from Intuit Developer portal
- Ensure QuickBooks Desktop is installed (the SDK depends on it)
- Run as Administrator if needed for COM registration

### "Access denied" or Authorization errors
- Open QuickBooks Desktop manually first
- Go to Edit → Preferences → Integrated Applications
- Remove any existing QB-TimeWarp entries and re-authorize

### "Company file is in use"
- Close QuickBooks Desktop on all other machines
- Ensure no other QBXML applications have an active session
- Try setting connection mode to `singleUser` in the code

### Import failures
- Check `Logs/` for detailed error messages
- Common causes: duplicate names, missing reference data, field length violations
- Set `"DryRun": true` to preview without importing
- Review `MigrationReport_*.json` for per-record failure details

### Financial reconciliation mismatches
- Balances depend on ALL transactions being imported successfully
- Check for skipped transactions in the migration report
- Opening balances may differ if the QB 2021 file isn't truly blank

### Journal entry validation failures
- **Unbalanced journal entries**: Check the source data in QB 2023. Review the `JournalMismatches` array in the validation report.
- **Invoice line item mismatches**: Can occur with tax or discount lines. Check if the invoice has tax lines not captured in standard line items.
- **Bill amount mismatches**: Verify all expense and item lines are being exported.
- **Payment application imbalances**: May occur with partial payments or credits.
- **Tolerance tuning**: Increase `Validation.ToleranceAmount` from 0.01 to 0.05 if you see many small rounding variances.

### Reactivation issues
- If entities remain inactive in QB 2021 after import, check the `ReactivationValidation` section in the validation report
- Ensure `TransformationRules.ReactivateInactiveEntities` is set to `true`
- Some entities may fail to import as active due to QB 2021 constraints

### Class tracking issues
- If class assignments are missing in QB 2021, ensure `TransformationRules.PreserveClassTracking` is `true`
- Check if class tracking is enabled in QB 2021 (Edit → Preferences → Accounting → Company Preferences)
- Review `ClassTrackingValidation` in the validation report for missing assignments
- Hierarchical classes (e.g., "Sales:West Coast") require parent classes to exist first — QB-TimeWarp handles this automatically

### Accounting model mismatch
- If the accounting method doesn't match, the SDK may not support modifying this preference
- Manually set it in QB 2021: Edit → Preferences → Accounting → Company Preferences
- The validation report will show the expected and actual methods

---

## NuGet Packages

| Package | Version | Purpose |
|---------|---------|---------|
| `Microsoft.Extensions.Configuration` | 6.0.1 | Configuration binding |
| `Microsoft.Extensions.Configuration.Json` | 6.0.0 | JSON config file support |
| `Microsoft.Extensions.Configuration.Binder` | 6.0.0 | Config binding to POCOs |
| `Newtonsoft.Json` | 13.0.3 | JSON serialization/deserialization |
| `Serilog` | 3.1.1 | Structured logging framework |
| `Serilog.Sinks.Console` | 5.0.1 | Console log output |
| `Serilog.Sinks.File` | 5.0.0 | File log output |
| `Spectre.Console` | 0.48.0 | Rich console formatting |

---

## License

This tool is provided as-is for QuickBooks Desktop data migration. Always back up your company files before running any migration. Test with a copy of your data first.
