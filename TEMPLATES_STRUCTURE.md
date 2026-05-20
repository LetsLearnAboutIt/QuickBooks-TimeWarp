# QB-TimeWarp Templates Directory

**Location:** `C:\QB-TimeWarp\Templates\`

## Purpose

This directory contains pristine template copies of QuickBooks company files used as the source for the Working Copy system. Templates are ONE-TIME copies from Golden Masters and are refreshed to Working directories during migration operations.

---

## File Hierarchy (3 Levels)

### Level 1: Golden Master (READ-ONLY)
**Location:** Desktop  
**Example:** `C:\Users\AIAgent\Desktop\QB21_Blank_Template\Blank Template.qbw`

- **Purpose:** Original, certified, pristine QuickBooks files
- **Access:** READ-ONLY — NEVER modified by QB-TimeWarp
- **Created By:** Manual user action (QuickBooks GUI)
- **Usage:** Source for ONE-TIME copy to Template directory

### Level 2: Template (PRISTINE COPY)
**Location:** `C:\QB-TimeWarp\Templates\`  
**Example:** `C:\QB-TimeWarp\Templates\QB2021_Blank\Blank_Template.qbw`

- **Purpose:** Pristine working copy, used as refresh source
- **Access:** Modified only during initial setup (copy from Golden Master)
- **Created By:** PowerShell copy operation or QB-TimeWarp setup
- **Usage:** Source for `--refresh` operations to Working directory

### Level 3: Working Copy (OPERATIONAL)
**Location:** `C:\QB-TimeWarp\Working\`  
**Example:** `C:\QB-TimeWarp\Working\Target\Blank_Template.qbw`

- **Purpose:** Active migration operations occur here
- **Access:** READ/WRITE — All QB-TimeWarp operations
- **Created By:** `--refresh` flag or AutoCreateWorkingCopies setting
- **Usage:** Actual import/export operations, testing, validation

---

## Workflow

```
┌─────────────────────────┐
│  Golden Master          │  Manual setup (QuickBooks GUI)
│  Desktop (READ-ONLY)    │  Certified blank template
│  Blank Template.qbw     │
└────────────┬────────────┘
             │ ONE-TIME COPY
             │ (PowerShell Copy-Item)
             ▼
┌─────────────────────────┐
│  Template               │  Pristine copy
│  Templates\QB2021_Blank │  Used as refresh source
│  Blank_Template.qbw     │
└────────────┬────────────┘
             │ --refresh
             │ (Every migration test)
             ▼
┌─────────────────────────┐
│  Working Copy           │  Active operations
│  Working\Target\        │  Import/Export/Validation
│  Blank_Template.qbw     │  Modified during tests
└─────────────────────────┘
```

---

## Current Templates

### QB2021_Blank
**Golden Master:** `C:\Users\AIAgent\Desktop\QB21_Blank_Template\Blank Template.qbw`  
**Template Copy:** `C:\QB-TimeWarp\Templates\QB2021_Blank\Blank_Template.qbw`  
**Created:** 2026-05-20  
**Size:** 13,254,656 bytes (~13.25 MB)  
**Status:** ✅ Certified blank template (Joseph verified)

**Purpose:** Clean QuickBooks 2021 company file for migration testing  
**Contains:** No pre-existing data, default chart of accounts only

---

## Adding New Templates

To add a new template:

1. **Create the template subdirectory:**
   ```powershell
   New-Item -ItemType Directory -Path "C:\QB-TimeWarp\Templates\<TemplateName>" -Force
   ```

2. **Copy golden master to template directory:**
   ```powershell
   Copy-Item "<GoldenMasterPath>\<FileName>.qbw" -Destination "C:\QB-TimeWarp\Templates\<TemplateName>\<FileName>.qbw"
   ```

3. **Verify the copy:**
   ```powershell
   Get-Item "C:\QB-TimeWarp\Templates\<TemplateName>\<FileName>.qbw" | Select-Object Name, Length, LastWriteTime
   ```

4. **Update `appsettings.json`:**
   ```json
   "TargetFiles": {
     "DesktopFolder": "C:\\QB-TimeWarp\\Templates\\<TemplateName>",
     "CompanyFileName": "*.qbw"
   }
   ```

5. **Document in this README**

---

## Safety Rules

1. **Golden Masters are READ-ONLY**
   - Never modify files on Desktop
   - Desktop is for originals only
   - Use Copy-Item to duplicate, never Move-Item

2. **Templates are REFRESH SOURCE**
   - Modified only during initial setup
   - Used as the source for `--refresh` operations
   - Should match Golden Master unless intentionally diverged

3. **Working Copies are EXPENDABLE**
   - Can be deleted and recreated at any time
   - `--refresh` flag overwrites with template copy
   - All migration operations happen here only

4. **ONE-DIRECTIONAL FLOW**
   - Golden Master → Template → Working
   - Never reverse: Working → Template → Golden Master
   - Each level protects the level above it

---

## Installer Portability

**Why Templates directory exists:**

- Desktop paths are user-specific (`C:\Users\AIAgent\Desktop\...`)
- Installer should use application-local directories
- `C:\QB-TimeWarp\Templates\` is part of the application bundle
- When deployed to production, templates travel with the application
- No dependency on specific user profiles or Desktop locations

**Deployment workflow:**
1. Install application to `C:\QB-TimeWarp\`
2. Templates directory included in installer
3. User provides golden master via Desktop/network/USB
4. Installer copies golden master → Templates (one-time)
5. All subsequent operations use Templates as source

---

## Troubleshooting

### Working copy corrupted
**Solution:** Run with `--refresh` flag to restore from template
```bash
C:\QB-TimeWarp\QB-TimeWarp.exe --refresh
```

### Template needs updating
**Solution:** Re-copy from golden master
```powershell
Copy-Item "C:\Users\AIAgent\Desktop\QB21_Blank_Template\Blank Template.qbw" -Destination "C:\QB-TimeWarp\Templates\QB2021_Blank\Blank_Template.qbw" -Force
```

### Golden master changed
**Steps:**
1. Update golden master on Desktop (via QuickBooks GUI)
2. Re-copy golden master → Template (see command above)
3. Run QB-TimeWarp with `--refresh` to update working copy

---

**Last Updated:** 2026-05-20  
**Maintained By:** Joseph (MSP Owner)  
**Project:** QB-TimeWarp — QuickBooks 2023 → 2021 Migration Tool
