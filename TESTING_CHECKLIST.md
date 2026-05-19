# QB-TimeWarp — Testing Checklist

> **Purpose**: Step-by-step testing guide for validating the migration pipeline. Follow each stage in order.

---

## ⚠️ Pre-Test: Critical Setup

- [ ] **Ensure QuickBooks windows remain VISIBLE throughout testing** — do NOT minimize them
- [ ] **Enter passwords when prompted** by QuickBooks:
  - Air-Masters-QB-2023.qbw → `3825You171`
  - Josh Safty 2021.qbw → `3825You171`
  - Blank Template → `Fl0640098!@!`
- [ ] **User is actively watching** — coordinate before each major stage
- [ ] Review `POPUP_HANDLING.md` for common popups you may encounter

---

## Stage 0: Working Copy Verification (SAFETY CHECK)

### 0.1 Verify Original Files Exist on Desktop
- [ ] Confirm `C:\Users\AIAgent\Desktop\Joshua's Gold Coast\` exists with a `.qbw` file
- [ ] Confirm `C:\Users\AIAgent\Desktop\Blank Template\` exists with a `.qbw` file
- [ ] Note the file sizes of both originals

### 0.2 Working Copy Creation
- [ ] Run QB-TimeWarp — observe "Initialize Working Copies (SAFETY FIRST)" step
- [ ] Verify `C:\QB-TimeWarp\Working\Source\` is created with a copy of the source `.qbw`
- [ ] Verify `C:\QB-TimeWarp\Working\Target\` is created with a copy of the target `.qbw`
- [ ] Verify file sizes in Working directories match original Desktop file sizes
- [ ] Confirm console shows "WORKING WITH COPIES — ORIGINALS PRESERVED"
- [ ] Confirm console shows "✓ Using working copy (originals protected)" for each QB connection

### 0.3 Safety Validation
- [ ] Verify original Desktop files are **unchanged** (same size, same modified date)
- [ ] Confirm no operations are logged against Desktop paths
- [ ] Test `--refresh` flag: re-run with `--refresh` and verify files are re-copied
- [ ] Test `--cleanup` flag: run with `--cleanup` and verify Working directories are removed

### 0.4 Protection Test (Optional)
- [ ] Temporarily change `appsettings.json` CompanyFilePath to a Desktop path
- [ ] Run QB-TimeWarp — should exit with code 99 (SAFETY VIOLATION)
- [ ] Restore correct configuration

---

## Prerequisites

- [ ] Windows VM is accessible via RDP (`aiagent.hostedremotedesktop.com:4420`)
- [ ] QuickBooks 2023 is installed and running
- [ ] QuickBooks 2021 is installed and running
- [ ] **Working copies created** in `C:\QB-TimeWarp\Working\` (automatic on first run)
- [ ] **Both QuickBooks windows are VISIBLE on screen** (not minimized)
- [ ] Application is built and deployed to `C:\QB-TimeWarp`
- [ ] `appsettings.json` has correct paths (verify SourceFiles and TargetFiles Desktop folders)

---

## Stage 1: Export Testing

### 1.1 Basic Export
- [ ] Run QB-TimeWarp — observe Step 1 (Export) in console
- [ ] Verify `.\ExportedData\` directory is created
- [ ] Verify JSON files are created for each entity type listed in `appsettings.json`
- [ ] **Expected**: One JSON file per entity type (e.g., `Accounts.json`, `Customers.json`, etc.)

### 1.2 Inactive Entity Export
- [ ] Check console output for "Including inactive records" message
- [ ] Open any exported JSON file (e.g., `Customers.json`)
- [ ] Verify entities with `"IsActive": "false"` are present
- [ ] Check export manifest for `TotalActiveRecords` and `TotalInactiveRecords` counts
- [ ] **Expected**: Both active and inactive records are exported; inactive count > 0

### 1.3 Export Completeness
- [ ] Verify all 26 entity types in config have corresponding export files
- [ ] Verify entity counts are reasonable (cross-check with QB 2023 UI if possible)
- [ ] Check for any error messages in `.\Logs\` directory
- [ ] **Expected**: No errors; all entity types exported successfully

### 🛑 CHECKPOINT: STOP and check with user
> **Before proceeding to Stage 2**: Share export summary with user. Confirm entity counts look reasonable. Ask if any popups appeared that need attention.

---

## Stage 2: Transformation Testing

### 2.1 Inactive → Active Reactivation
- [ ] Check console output for transformation report
- [ ] Verify "Reactivated Entities" count is displayed and > 0
- [ ] Spot-check transformed data: all entities should have `"IsActive": "true"`
- [ ] **Expected**: All previously inactive entities are now active

### 2.2 Class Tracking
- [ ] Check console output for "Discovered X unique classes"
- [ ] Verify the class list is complete (compare with QB 2023 class list)
- [ ] **Expected**: All classes used in transactions are discovered

### 2.3 Format Preservation
- [ ] Check transformation report for format preservation statistics
- [ ] Verify date fields are in `yyyy-MM-dd` or `yyyy-MM-ddTHH:mm:ss` format
- [ ] Verify currency fields have 2 decimal places
- [ ] Verify phone numbers are ≤ 21 characters
- [ ] Verify postal codes are ≤ 13 characters with leading zeros preserved
- [ ] **Expected**: All format stats show "preserved" counts matching "processed" counts

### 2.4 Field Mapping
- [ ] Check for any truncation warnings in the log
- [ ] Verify no data was silently dropped
- [ ] **Expected**: Truncations are logged; no data loss without logging

### 🛑 CHECKPOINT: STOP and check with user
> **Before proceeding to Stage 3 (Import)**: This is the most critical step — data will be written to QB 2021. Share transformation report with user. Confirm all transformations look correct. Get explicit go-ahead before importing.

---

## Stage 3: Import Testing

### 3.1 Accounting Model Match
- [ ] Check console for "Accounting Method: [Cash/Accrual]" from source
- [ ] Verify target QB 2021 accounting method matches source
- [ ] **Expected**: Both should show same method (Cash or Accrual)

### 3.2 Class Creation
- [ ] Check console for "Created X classes in QB 2021"
- [ ] Open QB 2021 → Lists → Class List
- [ ] Verify all source classes exist (including hierarchical parent:child)
- [ ] **Expected**: All required classes present in QB 2021

### 3.3 Entity Import
- [ ] Watch console for import progress per entity type
- [ ] Check for any "Error" or "Failed" messages
- [ ] Verify import order follows dependency sequence (Accounts first, Transactions last)
- [ ] **Expected**: All entities import without errors

### 3.4 Import Error Handling
- [ ] If `SkipOnError: true`, check for skipped records in the log
- [ ] Review any skipped records — are they expected failures?
- [ ] **Expected**: Minimal skipped records; any skips are logged with reason

### 🛑 CHECKPOINT: STOP and check with user
> **After import completes**: Share import results with user. Ask them to spot-check QB 2021 for imported data. Confirm everything looks good before running validation.

---

## Stage 4: Validation Testing

### 4.1 Entity Count Verification
- [ ] Check validation report for entity count comparison
- [ ] Source count should equal target count for each entity type
- [ ] **Expected**: All entity counts match (or differences are explained)

### 4.2 Field-by-Field Comparison
- [ ] Check validation report for field comparison results
- [ ] Investigate any mismatches
- [ ] **Expected**: All fields match (within tolerance for amounts)

### 4.3 Financial Reconciliation
- [ ] Check total amounts for invoices, bills, payments
- [ ] Source totals should match target totals (within $0.01 tolerance)
- [ ] **Expected**: Financial totals reconcile

### 4.4 Journal Balance Validation ⚠️ CRITICAL
- [ ] Check validation report for journal entry balance results
- [ ] **Every** journal entry must have total debits = total credits
- [ ] Any imbalanced journals are CRITICAL issues — investigate immediately
- [ ] **Expected**: ALL journals balanced; 0 imbalanced entries

### 4.5 Format Preservation Validation
- [ ] Check format validation section of the report
- [ ] All date fields should pass QB 2021 format check
- [ ] All currency fields should have proper decimal precision
- [ ] All phone fields should be within length limits
- [ ] All postal code fields should be within length limits
- [ ] **Expected**: 0 format validation issues

---

## Post-Migration Verification (Manual in QuickBooks)

### 5.1 QuickBooks 2021 Spot Checks
- [ ] Open QB 2021 → Chart of Accounts — verify accounts match QB 2023
- [ ] Open Customer Center — verify customer list (including formerly inactive)
- [ ] Open Vendor Center — verify vendor list
- [ ] Check a sample invoice — verify amounts, dates, line items
- [ ] Check a sample bill — verify amounts, dates, line items
- [ ] Run P&L report — compare with QB 2023 P&L
- [ ] Run Balance Sheet — compare with QB 2023 Balance Sheet
- [ ] Run P&L by Class — verify class assignments are preserved

### 5.2 Financial Report Comparison
- [ ] P&L (current year): QB 2023 total = QB 2021 total
- [ ] Balance Sheet (current date): QB 2023 total = QB 2021 total
- [ ] A/R Aging: QB 2023 totals = QB 2021 totals
- [ ] A/P Aging: QB 2023 totals = QB 2021 totals

---

## How to Interpret Logs and Reports

### Log Files (`.\Logs\QB-TimeWarp-{Date}.log`)
- **[INF]**: Informational — normal operation
- **[WRN]**: Warning — non-critical issue (e.g., truncation, missing optional field)
- **[ERR]**: Error — something failed (investigate immediately)
- **[FTL]**: Fatal — application crashed (check stack trace)

### Validation Report (`.\Validation\report.json`)
```json
{
  "OverallResult": "Pass",        // or "Fail"
  "EntityCountVerification": {},   // count comparisons
  "FieldComparison": {},           // field-level diffs
  "FinancialReconciliation": {},   // amount comparisons
  "JournalValidation": {},         // debit/credit balance
  "FormatValidation": {}           // format compliance
}
```

- **OverallResult = "Pass"**: Migration is successful
- **OverallResult = "Fail"**: Check individual sections for failures
- **JournalValidation failures**: CRITICAL — do not proceed until resolved
- **FormatValidation failures**: Review severity — "Warning" may be acceptable, "Error" must be fixed

### Export Manifest
- Located in `.\ExportedData\manifest.json`
- Contains entity counts, active/inactive breakdowns, export timestamp
- Use to verify export completeness before proceeding to transform

---

## Troubleshooting Common Issues

| Symptom | Likely Cause | Fix |
|---------|-------------|-----|
| "COM object" error | QB not running or wrong architecture | Ensure QB is running; verify x86 build |
| "Company file not found" | Wrong path in config | Check `appsettings.json` CompanyFilePath |
| "Access denied" to company file | Multi-user mode or permissions | Switch to single-user mode in QB |
| Entities skipped during import | Dependency not yet imported | Verify import order in config |
| Journal imbalance | Rounding during transform | Check currency precision settings |
| Date format error | Timezone not stripped | Verify `stripTimezoneFromDates: true` in FieldMappings.json |

---

## Troubleshooting: QuickBooks Popups During Testing

> **QuickBooks frequently generates popups that can block SDK operations.** Keep windows visible and watch for these.

| Popup | What to Do |
|-------|-----------|
| **"An application wants to access QuickBooks"** (SDK permission) | Click **"Yes, always allow access"** — this authorizes the QBXML SDK connection |
| **Company file password prompt** | Enter the correct password (see Pre-Test section above) |
| **"Another user is logged in"** / Multi-user warning | Switch to **single-user mode**: File → Switch to Single-user Mode |
| **"QuickBooks needs to update"** | Click **"Skip"** or **"Remind me later"** — do NOT update during testing |
| **"Registration" or "License" dialog** | Dismiss or close — do NOT proceed with registration during testing |
| **"Do you want to back up?"** on close | Click **"No"** unless you specifically want a backup |
| **Unrecognized popup** | **STOP** — screenshot it and check with user before clicking anything |

> **See `POPUP_HANDLING.md` for a comprehensive popup reference guide.**
