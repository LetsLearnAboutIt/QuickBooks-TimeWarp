# QB-TimeWarp Templates Structure

**Last Updated:** 2026-05-20  
**Status:** ✅ Corrected - Templates as Golden Copy

---

## 📂 Directory Hierarchy (2 Levels)

QB-TimeWarp uses a **2-level self-contained file structure** to protect certified templates and enable clean, repeatable testing:

```
C:\QB-TimeWarp\
├── Templates\          ← Level 1: GOLDEN COPY (READ-ONLY)
│   ├── QB2021_Blank\
│   │   └── Blank_Template.qbw (13.25 MB)
│   └── README.txt      ← Explains golden copy structure
│
└── Working\            ← Level 2: OPERATIONAL (MODIFIED BY TESTS)
    ├── Source\
    │   └── [Customer .qbw file - copied in]
    └── Target\
        └── [Blank template - refreshed from Templates\]
```

---

## 🔐 Level 1: Templates\ Directory (GOLDEN COPY)

**Location:** `C:\QB-TimeWarp\Templates\`  
**Purpose:** Contains certified, pristine QuickBooks blank templates  
**Status:** READ-ONLY - NEVER modified by QB-TimeWarp automation  
**Source:** Created manually via QuickBooks GUI by Joseph  
**Created:** 2026-05-20  
**Usage:** Source for `--refresh` operations only  

### What It Contains

| Path | File | Size | Purpose |
|------|------|------|---------|
| `Templates\QB2021_Blank\` | `Blank_Template.qbw` | 13.25 MB | Certified blank QB 2021 template |
| `Templates\` | `README.txt` | 2.6 KB | Directory documentation |

### Critical Rules

- ✅ **DO:** Use `--refresh` flag to copy FROM Templates\ to Working\
- ✅ **DO:** Keep Templates\ as pristine master copy
- ❌ **DON'T:** Run QB-TimeWarp.exe pointing directly at Templates\ files
- ❌ **DON'T:** Modify Templates\ files manually
- ❌ **DON'T:** Copy files from Desktop into Templates\ (Desktop is obsolete)

---

## ⚙️ Level 2: Working\ Directory (OPERATIONAL)

**Location:** `C:\QB-TimeWarp\Working\`  
**Purpose:** Active working copies for all migration operations  
**Status:** OPERATIONAL - Gets refreshed from Templates\  
**Usage:** All export/import/validation happens here  
**Safety:** Can be safely overwritten, corrupted, or tested  

### What It Contains

| Subdirectory | Content | Source |
|--------------|---------|--------|
| `Working\Source\` | Customer QB 2023 file | Copied from customer-provided file |
| `Working\Target\` | Blank QB 2021 template | **Refreshed FROM Templates\ directory** |

### Workflow

```
1. --refresh flag triggered
   └─ Templates\QB2021_Blank\Blank_Template.qbw
      └─ COPIED TO →
         └─ Working\Target\Blank_Template.qbw

2. Customer file copied
   └─ [Customer provides .qbw file]
      └─ COPIED TO →
         └─ Working\Source\Customer.qbw

3. Migration operations
   └─ ALL operations happen in Working\ directory
   └─ Templates\ remains completely untouched

4. If test fails
   └─ Run --refresh again
   └─ Fresh copy from Templates\ → Working\
   └─ Start over with pristine template
```

---

## 🎯 Self-Contained System

Everything QB-TimeWarp needs is in **`C:\QB-TimeWarp\`** except:

- Customer source `.qbw` files (provided by client - ONLY external input)
- QuickBooks application itself (installed separately on Windows)

### What About Desktop?

**Desktop was used temporarily for initial template certification only.**

- ❌ Desktop is **NOT** part of the normal workflow
- ❌ Desktop files are **obsolete** after Templates\ was created
- ✅ **Templates\** IS the source of truth
- ✅ All operations use Templates\ → Working\ flow

---

## 📋 Migration Workflow (Strictly Enforced)

```
┌─────────────────────────────────────────────────────────────┐
│ Step 0: Prepare Environment                                 │
│  └─ Run: QB-TimeWarp.exe --refresh                          │
│     └─ Action: Templates\ → Working\ (fresh copies)         │
└─────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────┐
│ Step 1: Load Schema (NO QB NEEDED)                          │
│  └─ Load cached schema from Schemas\QB_Schema_QB_2021.json  │
└─────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────┐
│ Step 2: Export from QB 2023                                 │
│  └─ Open: Working\Source\Customer.qbw (QB 2023)             │
│  └─ Export all data → ExportedData\                         │
│  └─ CLOSE QB 2023 completely                                │
└─────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────┐
│ Step 3: Transform Data                                      │
│  └─ SDK 16.0 → 15.0 field adaptation                        │
│  └─ Strip timezone offsets                                  │
│  └─ Apply all fixes                                         │
└─────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────┐
│ Step 4: Import to QB 2021                                   │
│  └─ Open: Working\Target\Blank_Template.qbw (QB 2021)       │
│  └─ Import all data (staged by dependency order)            │
│  └─ CLOSE QB 2021 completely                                │
└─────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────┐
│ Step 5: Validation                                          │
│  └─ Verify balances, counts, integrity                      │
│  └─ Generate migration report                               │
└─────────────────────────────────────────────────────────────┘
```

---

## 🔄 --refresh Flag Behavior

The `--refresh` flag is the key to the self-contained system:

**Command:**
```powershell
C:\QB-TimeWarp\QB-TimeWarp.exe --refresh
```

**What It Does:**

1. **Preserves Templates\** (READ-ONLY - never touched)
2. **Overwrites Working\Target\** with fresh copy from Templates\
3. **Preserves Working\Source\** (customer file remains)
4. **Result:** Clean target template, ready for new migration test

**When to Use:**

- ✅ Before every migration test run
- ✅ After a failed test (to reset target template)
- ✅ When target template becomes corrupted
- ✅ When you need a guaranteed pristine starting point

**When NOT to Use:**

- ❌ If you want to preserve target modifications for inspection
- ❌ During active debugging (will lose current state)

---

## 📊 File Size Reference

| File | Location | Size | Status |
|------|----------|------|--------|
| Blank_Template.qbw | Templates\QB2021_Blank\ | 13.25 MB | ✅ Certified blank |
| Joshs_Gold_Coast_II_2023.qbw | [Customer-provided] | ~30 MB | ✅ Test file |
| Air_Masters_QB_2023.qbw | [Customer-provided] | ~360 MB | ⚠️ Production (don't test!) |

---

## 🛡️ Safety Features

1. **Templates\ Protection**
   - Never modified by automation
   - Always available as pristine backup
   - Source of truth for all tests

2. **Working\ Isolation**
   - All destructive operations happen here
   - Can be reset with single --refresh command
   - Protects both Templates\ and customer originals

3. **Customer File Protection**
   - Original customer files never modified
   - Working copies created in Working\Source\
   - Originals remain safe in their original location

4. **No Desktop Dependency**
   - System doesn't rely on Desktop paths
   - All paths are application-local (C:\QB-TimeWarp\)
   - Installer-friendly structure

---

## 📝 Key Takeaways

| Concept | Location | Status | Modification |
|---------|----------|--------|--------------|
| **Golden Copy** | Templates\ | Pristine | NEVER |
| **Operational** | Working\ | Active | Always |
| **Customer Files** | External | Protected | NEVER |
| **Desktop** | Obsolete | Unused | N/A |

**Remember:**

- Templates\ = GOLDEN COPY (never modified)
- Working\ = OPERATIONAL (gets refreshed from Templates\)
- Customer files = EXTERNAL (only thing copied from outside)
- Desktop = TEMPORARY (used only for one-time certification - now obsolete)

---

**For questions, contact Joseph (MSP Owner, 40-year programmer)**
