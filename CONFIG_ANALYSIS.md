# QB-TimeWarp Configuration Analysis
**Date**: May 20, 2026  
**Analysis of**: appsettings.json and ARCHITECTURE.md updates

---

## Summary of Changes

### 1. Working Copy System Implementation

The configuration now implements a **3-tier safety hierarchy**:

```
Golden Master (Desktop, READ-ONLY)
    ↓
Template (C:\QB-TimeWarp\Templates\, Certified/Known-Good)
    ↓
Working Copy (C:\QB-TimeWarp\Working\, Operational)
```

### 2. Key Configuration Changes in appsettings.json

#### **Source File Paths** (QB 2023):
- **Desktop Golden Master**: `C:\Users\AIAgent\Desktop\Joshs_Gold_Coast\*.qbw` (READ-ONLY)
- **Working Copy**: `C:\QB-TimeWarp\Working\Source\placeholder.qbw` (OPERATIONAL)

#### **Target File Paths** (QB 2021):
- **Template Location**: `C:\QB-TimeWarp\Templates\QB2021_Blank\*.qbw` (Certified blank)
- **Working Copy**: `C:\QB-TimeWarp\Working\Target\placeholder.qbw` (OPERATIONAL)

#### **New WorkingDirectories Section**:
```json
"WorkingDirectories": {
  "SourcePath": "C:\\QB-TimeWarp\\Working\\Source",
  "TargetPath": "C:\\QB-TimeWarp\\Working\\Target",
  "AutoCreateWorkingCopies": true,
  "PreserveOriginals": true
}
```

#### **QuickBooks Connection Paths**:
- **QB2023**: Points to `C:\QB-TimeWarp\Working\Source\placeholder.qbw`
- **QB2021**: Points to `C:\QB-TimeWarp\Working\Target\placeholder.qbw`

Both use placeholders that will be replaced with actual filenames at runtime.

---

## Configuration Correctness Analysis

### ✅ **Strengths**:

1. **Absolute Paths Throughout**: All paths use `C:\QB-TimeWarp\` prefix, eliminating CWD ambiguity
2. **Safety Layer Separation**: Clear distinction between Templates (certified) and Working (operational)
3. **Original Preservation**: `PreserveOriginals: true` ensures templates are never modified
4. **Auto-Copy Feature**: `AutoCreateWorkingCopies: true` automates the Template → Working copy process
5. **Consistent Naming**: Application names clearly identify purpose:
   - "QB-TimeWarp Exporter" for QB 2023
   - "QB-TimeWarp Importer" for QB 2021

### ⚠️ **Potential Issues**:

1. **Path Discrepancy**: 
   - Context mentions `C:\QB-TimeWarp\Working\QB_Blank_Template\`
   - Config shows `C:\QB-TimeWarp\Working\Target\`
   - **Resolution Needed**: Confirm if this is intentional or if paths need alignment

2. **Placeholder Naming**:
   - Both QB versions use `placeholder.qbw` as the CompanyFilePath
   - Need to verify runtime logic replaces these with actual filenames

3. **Template Source**:
   - `TargetFiles.DesktopFolder` points to `C:\QB-TimeWarp\Templates\QB2021_Blank`
   - Assumes the blank template already exists at this location
   - **Action Required**: Verify blank templates exist in Templates folder before --refresh

---

## Architecture.md Changes

The ARCHITECTURE.md file shows **no recent changes** related to the Working Copy system. The current version documents:

- **4-stage pipeline**: Export → Transform → Import → Validate
- **Service layer breakdown** for all major components
- **Data flow diagrams** showing QB 2023 → JSON → QB 2021 migration

**Recommendation**: Update ARCHITECTURE.md to include:
1. Working Copy System section
2. Template hierarchy documentation
3. --certify mode workflow
4. --refresh flag behavior

---

## Readiness for --certify Mode

### Prerequisites Checklist:

- [ ] **Blank templates exist** at `C:\QB-TimeWarp\Templates\QB2021_Blank\`
- [ ] **Working directories created**: `C:\QB-TimeWarp\Working\Source\` and `C:\QB-TimeWarp\Working\Target\`
- [ ] **SDK permissions revoked** on blank templates (per Joseph's note)
- [ ] **QB 2021 closed** on Windows VM
- [ ] **.NET 6.0 SDK** available via system PATH (`dotnet --version`)

### Expected --certify Behavior:

1. Copy blank template from Templates → Working/Target
2. Launch QB 2021 with the working copy
3. Attempt SDK connection
4. **Trigger SDK authorization popup** (first-time connection)
5. User manually approves via GUI checkbox
6. SDK certificate stored in QB 2021
7. Exit without performing migration

---

## Recommended Actions Before --certify

1. **Verify Template Location**:
   ```powershell
   Get-ChildItem "C:\QB-TimeWarp\Templates\QB2021_Blank\" -Filter *.qbw
   ```

2. **Create Working Directories** (if not auto-created):
   ```powershell
   New-Item -ItemType Directory -Path "C:\QB-TimeWarp\Working\Source" -Force
   New-Item -ItemType Directory -Path "C:\QB-TimeWarp\Working\Target" -Force
   ```

3. **Verify .NET Installation**:
   ```powershell
   dotnet --version
   ```

4. **Confirm SDK Revocation**: 
   - Open QB 2021
   - Go to Edit → Preferences → Integrated Applications → Company Preferences
   - Verify "QB-TimeWarp Importer" is NOT in the approved list
   - Close QB 2021

---

## Configuration Summary

| Setting | Value | Status |
|---------|-------|--------|
| Source Golden Master | `C:\Users\AIAgent\Desktop\Joshs_Gold_Coast\*.qbw` | ✅ READ-ONLY |
| Target Template | `C:\QB-TimeWarp\Templates\QB2021_Blank\*.qbw` | ⚠️ Verify exists |
| Working Source | `C:\QB-TimeWarp\Working\Source\` | ✅ Auto-created |
| Working Target | `C:\QB-TimeWarp\Working\Target\` | ✅ Auto-created |
| QB2023 Connection | `C:\QB-TimeWarp\Working\Source\placeholder.qbw` | ✅ Absolute path |
| QB2021 Connection | `C:\QB-TimeWarp\Working\Target\placeholder.qbw` | ✅ Absolute path |
| SDK Version (2023) | 16.0 | ✅ Correct |
| SDK Version (2021) | 15.0 | ✅ Correct |
| Application Name (Import) | "QB-TimeWarp Importer" | ✅ Distinct |
| Auto-Create Copies | true | ✅ Enabled |
| Preserve Originals | true | ✅ Enabled |

---

## Next Steps

1. ✅ **Configuration reviewed** - Structure looks solid
2. ⏳ **Connect to Windows VM** - Verify prerequisites
3. ⏳ **Run --certify mode** - Trigger fresh SDK authorization
4. ⏳ **Document certificate approval** - Capture the authorization flow
5. ⏳ **Update ARCHITECTURE.md** - Document Working Copy system

---

**Overall Assessment**: Configuration is well-structured and implements proper safety layers. The absolute path strategy eliminates ambiguity. Minor discrepancy with context-mentioned path needs clarification, but does not block --certify testing.
