# QB-TimeWarp — Change Log

> **Purpose**: Chronological record of all changes made to the project, with reasoning and affected files.

---

## Change 1: Initial Implementation — QBXML SDK Integration

**What**: Created the foundational project structure with QBXML SDK integration for QuickBooks data migration.

**Why**: No native tool exists for backward migration from QB 2023 → QB 2021. The QBXML SDK is the only reliable programmatic interface to QuickBooks.

**Files Created**:
- `QB-TimeWarp.csproj` — Project file targeting .NET 6.0, x86, with NuGet packages (Newtonsoft.Json, Serilog, Spectre.Console)
- `Program.cs` — Main orchestrator with 4-stage pipeline (Export → Transform → Import → Validate)
- `Services/QBConnectionManager.cs` — COM Interop wrapper for QBXML SDK connections
- `Services/DataExporter.cs` — Exports entities from QB 2023 to JSON
- `Services/DataTransformer.cs` — Transforms data for QB 2021 compatibility
- `Services/DataImporter.cs` — Imports transformed JSON into QB 2021
- `Services/DataValidator.cs` — Post-import validation
- `Services/SchemaExtractor.cs` — Extracts field schemas from QB 2021
- `Models/ConfigurationModels.cs` — Configuration POCOs
- `Models/MigrationModels.cs` — Data transfer models
- `Models/ValidationModels.cs` — Validation report models
- `Models/FieldMappingModels.cs` — Field mapping models
- `Models/QBEntityType.cs` — Entity type definitions
- `Helpers/ProgressReporter.cs` — Console progress bars and banners
- `Helpers/TransformFunctions.cs` — Transformation utility functions
- `appsettings.json` — Application configuration
- `Configuration/FieldMappings.json` — Field mapping rules
- `README.md` — Project documentation
- `.gitignore` — Git ignore rules

**Configuration**: Base `appsettings.json` created with QB 2023/2021 paths, export/import settings, and logging configuration.

---

## Change 2: Inactive Entity Export (`ActiveStatus=All` Filter)

**What**: Updated `DataExporter.cs` to include inactive entities in the export by adding `ActiveStatus=All` to QBXML queries for entity types that support it.

**Why**: The client has inactive customers, vendors, items, etc. in QB 2023. Without exporting them, historical transactions referencing these entities would fail to import or lose referential integrity.

**Files Modified**:
- `Services/DataExporter.cs` — Added `ActiveStatusEntityTypes` HashSet; modified `ExportEntityType()` to inject `<ActiveStatus>All</ActiveStatus>` into QBXML queries; added active/inactive count tracking; added `LogInactiveEntitySummary()` method
- `Models/MigrationModels.cs` — Added `ActiveCount` and `InactiveCount` properties to `ExportedEntitySet`
- `appsettings.json` — Added `"IncludeInactiveRecords": true` to Export section

**Configuration Update**: Set `Export.IncludeInactiveRecords = true` in `appsettings.json`.

---

## Change 3: Journal Validation for Debit/Credit Matching

**What**: Added comprehensive journal entry validation to ensure all journal entries balance (total debits = total credits within tolerance).

**Why**: Accountant's critical requirement. Unbalanced journal entries indicate data corruption and would be rejected by QB 2021. This validation catches issues before they become accounting problems.

**Files Modified**:
- `Services/DataValidator.cs` — Added journal validation logic that checks each journal entry's debit and credit line totals
- `Models/ValidationModels.cs` — Added journal validation report models
- `Models/ConfigurationModels.cs` — Added `EnableJournalValidation` boolean property (defaults to `true`)
- `appsettings.json` — Added `"EnableJournalValidation": true` to Validation section

**Configuration Update**: Set `Validation.EnableJournalValidation = true` in `appsettings.json`.

---

## Change 4: Inactive-to-Active Transformation Rule

**What**: Added transformation rule that converts all inactive entities to active during the Transform stage.

**Why**: Accountant requirement for clean migration. QB 2021 may reject imports of inactive entities, and the accountant wants all records active for initial review. They can deactivate records manually after verifying the migration.

**Files Modified**:
- `Services/DataTransformer.cs` — Added logic to set `IsActive=true` on all entities when `ReactivateInactiveEntities` is enabled; tracks reactivation count in `TransformationReport`
- `Models/MigrationModels.cs` — Added `ReactivatedCount` to `TransformationReport`
- `Models/ConfigurationModels.cs` — Added `TransformationRulesConfig` with `ReactivateInactiveEntities` property
- `appsettings.json` — Added `TransformationRules.ReactivateInactiveEntities = true`
- `Program.cs` — Updated to pass `TransformationRules` config to `DataTransformer`; displays reactivation summary

**Configuration Update**: Added `TransformationRules` section to `appsettings.json`.

---

## Change 5: Class Tracking Preservation

**What**: Added class tracking preservation — discovers classes used in transactions, checks if they exist in QB 2021, and auto-creates missing ones before import.

**Why**: Classes are used for cost center reporting (departments, locations, projects). Losing class assignments would break the accountant's P&L by Class reports and other departmental financial reports.

**Files Modified**:
- `Services/DataTransformer.cs` — Added class discovery logic that scans all transactions for `ClassName`/`ClassRef` fields; collects unique classes
- `Services/DataImporter.cs` — Added `EnsureClassesExist()` method that queries QB 2021 for existing classes, creates missing ones via `ClassAddRq` (handles hierarchical parent:child classes); added `QueryExistingClasses()` and `CreateClassInQB2021()` methods
- `Models/MigrationModels.cs` — Added `ClassTrackingSummary` model
- `Program.cs` — Added Step 4b: Class existence check before transaction import
- `appsettings.json` — Added `TransformationRules.PreserveClassTracking = true`

**Configuration Update**: Set `TransformationRules.PreserveClassTracking = true` in `appsettings.json`.

---

## Change 6: Accounting Model Matching

**What**: Added logic to query the accounting method (Cash/Accrual) from QB 2023 and apply it to QB 2021 using `PreferencesModRq`.

**Why**: If the source uses Accrual basis and the target uses Cash basis, all financial reports in QB 2021 would show incorrect numbers. The accounting method must match for data integrity.

**Files Modified**:
- `Services/DataImporter.cs` — Added `QueryCompanyPreferences()` to read preferences from QB 2021; added `ApplyAccountingPreferences()` to update accounting method via `PreferencesModRq`
- `Models/MigrationModels.cs` — Added `CompanyPreferences` model with AccountingMethod, ReportBasis, ClassTrackingEnabled, etc.
- `Program.cs` — Added Step 4a: Accounting preferences matching before import; logs source/target comparison
- `appsettings.json` — Added `TransformationRules.MatchAccountingModel = true`

**Configuration Update**: Set `TransformationRules.MatchAccountingModel = true` in `appsettings.json`.

---

## Change 7: Date/Field Format Preservation

**What**: Added comprehensive format preservation system for dates, currency, phone numbers, postal codes, and memo fields during transformation. Added corresponding validation.

**Why**: Data integrity during migration. Dates with timezones cause QB 2021 errors. Currency rounding errors compound across transactions. Postal codes losing leading zeros (01234 → 1234) break mailing. Phone numbers exceeding QB field limits get truncated poorly.

**Files Modified**:
- `Helpers/TransformFunctions.cs` — Added `FormatPreservationResult` class; added format-specific functions: `PreserveDate()`, `PreserveCurrencyFormat()`, `PreservePhoneFormat()`, `PreservePostalCodeFormat()`, `PreserveMemoFormat()`, `FormatAwareTruncate()`; added detection helpers: `IsDateField()`, `IsCurrencyField()`, `DetectDateFormat()`, `ValidateDateForQB2021()`
- `Services/DataTransformer.cs` — Refactored `ProcessValue()` to attempt format-aware processing first; added `DetectFormatType()` and `ApplyFormatPreservation()` methods; added `LogFormatPreservationSummary()`
- `Services/DataValidator.cs` — Added `ValidateFormatPreservation()` orchestrator; added `ValidateDateFieldFormats()`, `ValidateCurrencyFieldFormats()`, `ValidatePhoneFieldFormats()`, `ValidatePostalCodeFieldFormats()` methods
- `Models/MigrationModels.cs` — Added `FormatPreservationStats` class with per-type counts
- `Models/ValidationModels.cs` — Added `FormatValidationReport` and `FormatValidationIssue` classes
- `Models/FieldMappingModels.cs` — Added `FormatRulesConfig` class; added `FormatType` property to `FieldMapping`; added `FormatRules` to `FieldMappingsConfig`
- `Configuration/FieldMappings.json` — Added `formatRules` section with preservation toggles and field patterns
- `README.md` — Added comprehensive "Field Format Preservation" documentation section

**Configuration Update**: Added `formatRules` section to `Configuration/FieldMappings.json` with all format preservation toggles enabled by default.

---

## Change 8: QB 2023 Path Correction in Configuration

**What**: Updated `appsettings.json` to use the correct QB 2023 executable path.

**Why**: The original path pointed to the install directory; the corrected path points to the actual executable `QBWPremierAccountant.exe` which is needed for SDK connection.

**Files Modified**:
- `appsettings.json` — Changed `QB2023.InstallPath` from `C:\Program Files\Intuit\QuickBooks 2023` to `C:\Program Files\Intuit\Quickbooks 2023\QBWPremierAccountant.exe`

---

## Change 9: Comprehensive Project Documentation

**What**: Created a full documentation suite for project context retention across sessions.

**Why**: AI sessions are ephemeral. Without persistent documentation, each new session loses context about requirements, design decisions, connection details, and implementation history.

**Files Created**:
- `PROJECT_CONTEXT.md` — Complete project overview, requirements, design decisions, transformation rules
- `SESSION_RESUME.md` — Connection details, credentials, paths, build instructions
- `CHANGE_LOG.md` — This file: chronological change history
- `ARCHITECTURE.md` — System architecture, service layers, data flow
- `TESTING_CHECKLIST.md` — Comprehensive testing steps and expected results
- `README_FOR_NEW_SESSIONS.md` — Quick start guide for resuming work


### Change 10: Working Copy System — Original File Protection

**What**: Added automatic working copy creation system that copies original QB company files from Desktop to dedicated Working directories before any operations begin. Multiple safety layers prevent any modification to original files.

**Why**: Original QuickBooks company files (Joshs_Gold_Coast, QB21_Blank_Template, Air_Masters) must NEVER be directly modified. The working copy system ensures all operations use disposable copies while originals remain pristine on the Desktop.

**Files Modified**:
- `Services/WorkingDirectoryManager.cs` — NEW: Manages working directory creation, file copying, verification, and cleanup
- `Models/ConfigurationModels.cs` — Added `SourceFilesConfig`, `TargetFilesConfig`, `WorkingDirectoriesConfig` classes
- `appsettings.json` — Added `SourceFiles`, `TargetFiles`, `WorkingDirectories` sections; updated `CompanyFilePath` to Working paths
- `Program.cs` — Added working copy initialization before QB operations; added `--cleanup` and `--refresh` commands; added safety exception handling
- `Services/QBConnectionManager.cs` — Added `ValidateCompanyFilePath()` with protected Desktop folder patterns; blocks connections to originals
- `SAFETY_FEATURES.md` — NEW: Comprehensive documentation of all safety layers
- `SESSION_RESUME.md` — Added working copy folder structure section
- `TESTING_CHECKLIST.md` — Added Stage 0: Working Copy Verification
- `README.md` — Added Working Copy System section and safety feature description
- `CHANGE_LOG.md` — This entry

**Configuration**: Ensure `SourceFiles.DesktopFolder` and `TargetFiles.DesktopFolder` point to the correct Desktop folders. `WorkingDirectories.AutoCreateWorkingCopies` should be `true`.

**New CLI Options**: `--refresh` (force re-copy), `--cleanup` (delete working dirs)

**New Exit Code**: 99 = Safety violation (attempted operation on original file)

---


---

### Change 11: QBXML SDK 15.0 Compatibility — Fix 0x80040400 Errors

**Date**: 2026-05-19

**What**: Fixed QBXML parsing/generation to be fully compatible with QuickBooks 2021 (SDK 15.0). The trial run showed COM error 0x80040400 because QBXML was being generated using SDK 16.0 features that QB 2021 doesn't support.

**Why**: QB 2021 uses SDK 15.0 while QB 2023 uses SDK 16.0. Fields, entity types, and field lengths differ between versions. Sending SDK 16.0 QBXML to QB 2021 causes 0x80040400 errors (element not supported).

**Critical Fixes**:
- **AccountNumber preservation**: Accountant flagged missing account numbers — was being lost during transformation. Now explicitly preserved.
- **CC balance sign inversion**: Credit card balances with wrong sign are auto-corrected (positive = money owed).
- **CC columns**: Already fixed in prior session, verified in transformation.
- **Empty Name fields**: Entities with blank names now get auto-generated placeholder names.
- **Payroll items**: Complex QB 2023 payroll structures simplified for QB 2021 format.

**Files Modified**:
- `Helpers/QBSDKVersionHelper.cs` (NEW) — SDK version detection, field compatibility lists, error code explanations
- `Services/DataExporter.cs` — Auto-detects SDK version via HostQuery, adjusts QBXML version dynamically
- `Services/DataTransformer.cs` — Added 6 new compatibility methods: RemoveSDK16OnlyFields, EnforceQB2021FieldLengths, FixEmptyNameField, SimplifyPayrollFields, FixCCBalanceSigns, PreserveAccountNumbers
- `Services/DataImporter.cs` — Smart error handling (no retry for 0x80040400), dynamic field exclusion learning, pre-import validation mode, field length enforcement in QBXML generation
- `Services/QBConnectionManager.cs` — ProcessRequestWithRetry no longer retries version compatibility errors
- `Configuration/FieldMappings.json` — Added qb2021Compatibility section, fixed AccountNumber maxLength, marked SDK 16-only fields
- `PROJECT_INSTRUCTIONS.MD` (NEW) — Primary session briefing document

**New Features**:
- **SDK Version Detection**: Sends HostQuery to detect QB's supported SDK versions, auto-adjusts
- **Dynamic Field Exclusion**: When a field causes 0x80040400, it's automatically excluded from all subsequent imports
- **Pre-Import Validation**: `ValidateSampleBeforeImport()` tests sample records before full import
- **Compatibility Summary**: Transformation log now shows how many fields were adjusted for QB 2021

**Configuration**:
- `FieldMappings.json > qb2021Compatibility` documents all known incompatible fields
- `FieldMappings.json > globalSettings.targetSDKVersion` = "15.0"
- `FieldMappings.json > globalSettings.enforceQB2021FieldLimits` = true

**Error Codes Handled**:
- `0x80040400` / COM HRESULT -2147220480: Unsupported QBXML element
- QBXML status `3250`: Unsupported element in request
- QBXML status `3260`: Element not valid for this version
- QBXML status `3270`: Unsupported QBXML version



---

### Change 12: Root Cause Analysis & Documentation Overhaul

**Date**: 2026-05-19 (late session)

**What**: Completed comprehensive root cause analysis of import failures. Updated all documentation files to preserve full project understanding across sessions. Created CRITICAL_FIXES_NEEDED.MD.

**Why**: Trial run showed 0 genuine successes (359 phantom successes from detection bug). Five distinct root causes identified that must be fixed before import can succeed. All knowledge committed to files so session refresh loses nothing.

**Root Causes Identified**:
1. **Hierarchical names** — Full path sent as Name instead of leaf + ParentRef
2. **IsActive field** — Inactive entities rejected or break reference lookups
3. **Account types prerequisite** — Must exist before accounts
4. **Missing reference types** — Sales tax, terms, etc. must exist before referencing entities
5. **Field compatibility** — SDK 16.0 fields rejected by SDK 15.0
6. **(Bug) Success detection** — 359 phantom successes from incorrect response parsing

**Correct Order of Operations Defined**:
- Stage 0: Account types + missing reference types
- Stage 1: Accounts (with hierarchy)
- Stage 2: Entities (customers, vendors)
- Stage 3: Items
- Stage 4: Transactions

**Critical Transformation Timing Documented**:
- Export from QB 2023 → Close QB 2023 → Open QB 2021 → Transform → Import

**Files Created**:
- `CRITICAL_FIXES_NEEDED.MD` — All 5 fixes with priority, dependencies, implementation details

**Files Updated**:
- `PROJECT_INSTRUCTIONS.MD` — Added VM specs (Ryzen 9 9950X, 32GB, 2Gbps), emphasized Notepad Protocol, added SSH helper usage, added transformation timing, added order of operations
- `DEBUGGING_NOTES.MD` — Restructured with all 5 root causes clearly documented, correct stage ordering, fix history
- `CHANGE_LOG.md` — This entry

**Status**: Staged import architecture ✅ | Root cause analysis ✅ | Fixes identified ✅ | Implementation PENDING



---

### Change 13: File/Folder Rename — Remove Spaces from All Paths

**Date**: 2026-05-19
**What**: Joseph renamed all Desktop company file folders and files to use underscores instead of spaces. Updated all documentation, code, and configuration to match.

**Old → New Names**:
| Old Name | New Name |
|----------|----------|
| `Joshua's Gold Coast` | `Joshs_Gold_Coast` |
| `Blank Template` | `QB21_Blank_Template` |
| `Air Masters` | `Air_Masters` |
| `Air-Masters-QB-2023.qbw` | `Air_Masters_QB_2023.qbw` |

**Why**: Spaces in Windows file paths cause issues with command-line tools, batch scripts, Git operations, SSH transfers, and COM/SDK path resolution. Underscores eliminate the need for quoted paths everywhere.

**Files Updated**:
- `appsettings.json` — Updated `SourceFiles.DesktopFolder` and `TargetFiles.DesktopFolder`
- `Services/QBConnectionManager.cs` — Added new underscore paths to `ProtectedDesktopFolders` (kept legacy space paths for safety)
- `Services/WorkingDirectoryManager.cs` — Updated comments and log labels
- `Models/ConfigurationModels.cs` — Updated XML doc examples
- `Program.cs` — Updated folder structure comment
- `SESSION_RESUME.md` — Updated all path references and company file tables
- `SAFETY_FEATURES.md` — Updated folder structure, protected patterns, config example
- `TESTING_CHECKLIST.md` — Updated verification paths
- `README.md` — Updated folder structure diagram
- `README_FOR_NEW_SESSIONS.md` — Updated VM file paths table
- `PROJECT_INSTRUCTIONS.MD` — Updated VM key paths
- `CHANGE_LOG.md` — This entry
- `POPUP_HANDLING.md` — Updated Blank Template reference
- `DEBUGGING_NOTES.MD` — No path changes needed (uses generic references)
- `PATH_REFERENCE.md` — **NEW**: Quick reference for all file paths with old→new mapping

**CRITICAL**: `QBConnectionManager.cs` now blocks BOTH old (space) paths AND new (underscore) paths from direct access, ensuring safety even if old paths somehow persist.
