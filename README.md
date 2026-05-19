# QB-TimeWarp — QuickBooks 2023 → 2021 Data Migration Tool

A production-ready C# console application that migrates all data from a QuickBooks Desktop 2023 company file to a QuickBooks Desktop 2021 company file using the QBXML SDK.

---

## Features

- **Full Data Export** — Exports all entities from QB 2023: Customers, Vendors, Accounts, Items, Employees, and all transaction types (Invoices, Bills, Payments, Journal Entries, Sales Receipts, Purchase Orders, Credit Memos, Estimates, Deposits, Checks, Vendor Credits, Inventory Adjustments, Transfers)
- **Schema Extraction** — Documents QB 2021's supported fields, data types, and constraints as JSON
- **Configurable Field Mapping** — Human-editable `FieldMappings.json` maps QB 2023 fields to QB 2021 equivalents with support for renaming, transformations, defaults, and skipping deprecated fields
- **Dependency-Ordered Import** — Imports lists before transactions, respecting referential integrity (Accounts → Customers/Vendors → Items → Invoices/Bills → Payments)
- **Comprehensive Validation** — Field-by-field comparison, entity count verification, and financial totals reconciliation (account balances, AR/AP, transaction sums)
- **Error Recovery** — Skips problematic records and continues; logs detailed error reasons for every failure
- **Rich Console Output** — Step-by-step progress indicators, ASCII art banner, per-entity status breakdown
- **Structured Logging** — Serilog-based logging to both console and timestamped log files

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
│   ├── MigrationModels.cs              # Core data models (QBEntity, ImportResult, etc.)
│   ├── ValidationModels.cs             # Validation report models
│   ├── ConfigurationModels.cs          # Configuration binding models
│   └── FieldMappingModels.cs           # Field mapping configuration models
│
├── Services/
│   ├── QBConnectionManager.cs          # QBXML SDK connection & session management
│   ├── SchemaExtractor.cs              # QB 2021 schema discovery
│   ├── DataExporter.cs                 # QB 2023 data export
│   ├── DataTransformer.cs              # Data transformation engine
│   ├── DataImporter.cs                 # QB 2021 data import
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
          "sourceField": "JobStatus",
          "targetField": "JobStatus",
          "action": "transform",
          "transformFunction": "MapJobStatus",
          "notes": "QB 2023 may have new job statuses not in 2021"
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
| `Validation/` | `MigrationReport_*.json` — overall migration summary |
| `Logs/` | Timestamped log files with full operation details |

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
