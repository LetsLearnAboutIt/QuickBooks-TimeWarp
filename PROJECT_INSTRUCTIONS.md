# PROJECT_INSTRUCTIONS.md — QB-TimeWarp

---

## 🔥 READ FIRST: `/home/ubuntu/memory.md`

> **Before doing ANYTHING else**, read `/home/ubuntu/memory.md`.
> That is the ONE AND ONLY memory file (not in this project dir).
> It contains all passwords, connection details, paths, current status,
> and the complete quick-start checklist for immediate session resumption.

---

## 📚 ALSO READ THESE SUPPLEMENTAL FILES

Before starting work, check for and read these files if they exist:

### Always Read
| File | Purpose |
|------|---------|
| `/home/ubuntu/memory.md` | 🧠 THE memory file — passwords, paths, status, next steps |
| `PROJECT_CONTEXT.md` | What the project does, design decisions, requirements |
| `CHANGE_LOG.md` | Full chronological history of every change made |

### Supplemental Files (ALL in /home/ubuntu/, NOT the project dir!)
```bash
# Run this EVERY session to read supplemental files:
cat /home/ubuntu/memory.md
cat /home/ubuntu/PROJECT_INSTRUCTIONS_ADDITION.md 2>/dev/null
cat /home/ubuntu/DEBUGGING_NOTES.md 2>/dev/null
cat /home/ubuntu/CHANGE_LOG_ADDITION.md 2>/dev/null
cat /home/ubuntu/DEBUGGING_SUMMARY_FOR_JOSEPH.md 2>/dev/null
cat /home/ubuntu/debugging_review_report.md 2>/dev/null
```

| File | Location | Purpose |
|------|----------|---------|
| **`memory.md`** | `/home/ubuntu/` | THE canonical memory file — all credentials, paths, status |
| **`PROJECT_INSTRUCTIONS_ADDITION.md`** | `/home/ubuntu/` | Root cause analysis of import regression — 2 bugs, QBXML requirements, fix priorities |
| **`DEBUGGING_NOTES.md`** | `/home/ubuntu/` | Deep debugging: success detection bug, QBXML parsing errors, code architecture notes |
| **`CHANGE_LOG_ADDITION.md`** | `/home/ubuntu/` | Regression timeline: what broke in Turn 45, 359 phantom successes discovery |
| **`DEBUGGING_SUMMARY_FOR_JOSEPH.md`** | `/home/ubuntu/` | Concise debugging summary formatted for Joseph |
| **`debugging_review_report.md`** | `/home/ubuntu/` | Debugging review report |

### Legacy Files in Project Dir (still valid context)
| File | Purpose |
|------|---------|
| **`PROJECT_INSTRUCTIONS.MD`** (uppercase) | Original agent briefing from earlier session |
| **`DEBUGGING_NOTES.MD`** (uppercase) | Debugging notes copy in project dir |

### Reference As Needed

---

## 🚀 CURRENT IMPLEMENTATION STATUS (Updated 2026-05-19)

### Critical Infrastructure - Schema Caching
**Status:** ✅ ACTIVE (Commit `26e82b9`)

The QB 2021 schema is extracted **ONE-TIME ONLY** and cached at:
```
C:\QB-TimeWarp\Schemas\QB_Schema_QB_2021.json
```

**Why:** QB 2021's schema (supported fields, data types, XSD ordering) never changes. Once extracted, we load it from the cached file instead of opening QB 2021 every time.

**Impact:** Step 1 now loads instantly without requiring QB 2021 connection.

---

### Critical Workflow - Sequential QB Operations
**Status:** ✅ ENFORCED (Commit `a18d0da`)

**RULE: Only ONE QuickBooks instance can be open at a time for SDK operations.**

```
Step 1: Load Schema
   └─ NO QB NEEDED (load from cached file)

Step 2: Export from QB 2023
   ├─ Open QB 2023
   ├─ Export all data
   └─ CLOSE QB 2023 completely

Step 3: Transform Data
   └─ NO QB NEEDED (pure data transformation)

Step 4: Import to QB 2021
   ├─ Open QB 2021
   ├─ Import all data
   └─ CLOSE QB 2021 completely

Step 5: Validation
   └─ Compare results, verify balances
```

**What Changed:** 
- Pre-import validation (Step 3.5) DISABLED — it tried to open both QB 2023 AND QB 2021 simultaneously, violating the workflow
- Code commented out with detailed explanation in `Program.cs` lines 307-359

---

### Critical Fix #7 - Timezone Offset Stripping
**Status:** ✅ ACTIVE (Commit `1f18a89`)

**Problem:** QBXML does NOT support timezone offsets in date fields.
- QB 2023 exports: `2026-05-10T23:29:48-06:00` ❌
- QBXML requires: `2026-05-10T23:29:48` ✓

**Solution:** Strip all timezone suffixes (`-06:00`, `+05:30`, `Z`) from date/datetime fields before building QBXML.

**Impact:** Fixes 1,634 out of 2,072 previously failing records (TimeCreated, TimeModified, TxnDate, DueDate, etc.)

**Implementation:**
- Added `IsDateTimeField()`, `ContainsTimezoneOffset()`, `StripTimezoneOffset()` helper methods
- Applied in `BuildFieldsXml()` and `BuildLineItemsXml()`
- See `Services/DataImporter.cs` lines 2008-2012, 2108-2147

---

### All 7 Critical Fixes - Summary
| # | Fix | Commit | Status |
|---|-----|--------|--------|
| 1 | Hierarchical name parsing | `d0c9ffd` | ✅ ACTIVE |
| 2 | Force IsActive=true | `d0c9ffd` | ✅ ACTIVE |
| 3 | Account type verification | `d0c9ffd` | ✅ ACTIVE |
| 4 | Create missing reference types | `d0c9ffd` | ✅ ACTIVE |
| 5 | Skip SDK 16.0-only fields | `d0c9ffd` | ✅ ACTIVE |
| 6 | Success detection fix | `d0c9ffd` | ✅ ACTIVE |
| 7 | Strip timezone offsets | `1f18a89` | ✅ ACTIVE |

---

### GitHub Repository
**URL:** https://github.com/LetsLearnAboutIt/QuickBooks-TimeWarp

**Latest Commits:**
- `a18d0da` - Remove QB 2021 from export phase, enforce strict workflow
- `1f18a89` - FIX #7: Strip timezone offsets from QBXML dates
- `26e82b9` - Add schema caching (ONE-TIME extraction)
- `d0c9ffd` - Fixes 1-6 implementation

**Workflow:** All development happens on Linux (`/home/ubuntu/QB-TimeWarp/`), push to GitHub, pull on Windows VM (`C:\QB-TimeWarp\`)

---
| File | Purpose |
|------|---------|
| `PATH_REFERENCE.md` | All file paths + old→new rename mapping |
| `ARCHITECTURE.md` | Code structure, service layers, data flow |
| `TESTING_CHECKLIST.md` | Step-by-step testing procedures |
| `POPUP_HANDLING.md` | How to handle QuickBooks popups |
| `SAFETY_FEATURES.md` | Multi-layer protection system docs |
| `SESSION_RESUME.md` | Connection details (also in memory.md) |
| `README_FOR_NEW_SESSIONS.md` | Quick-start guide |
| `PASSWORDS.txt` | All credentials (gitignored) |

---

## 🎯 PROJECT GOAL

Migrate data from **QuickBooks 2023** (SDK 16.0) to **QuickBooks 2021** (SDK 15.0) via a .NET 6.0 C# application that:

1. **Exports** all entity data from QB 2023 as JSON
2. **Transforms** the data for QB 2021 compatibility (field mapping, format preservation, SDK version adaptation)
3. **Imports** the transformed data into QB 2021 using staged, dependency-aware import
4. **Validates** the migration (entity counts, field comparison, financial reconciliation, format checks)

### Key Constraints
- QBXML SDK is 32-bit only → build must target **x86**
- QuickBooks must be running with company file open for SDK to connect
- **Single-user mode** recommended for reliable SDK access
- Originals are NEVER modified — working copy system protects them

---

## ⚠️ CRITICAL BUILD NOTES

```bash
# dotnet is NOT in PATH — always use full path:
/home/ubuntu/.dotnet/dotnet build

# Always build the PROJECT, never look for a .sln:
cd /home/ubuntu/QB-TimeWarp
/home/ubuntu/.dotnet/dotnet build    # uses QB-TimeWarp.csproj automatically

# Expected result: 0 Errors, 1 Warning (CS8602 in QBConnectionManager.cs:385)
```

---

## 🤝 WORKING WITH JOSEPH

- Joseph is a **40-year programmer** and MSP owner — he knows what he's talking about
- He watches the VM screen and programs alongside you — this is **collaborative**
- He wants **precise, complete documentation** with no shortcuts or placeholders
- **Don't barrel through** — pause at key stages and coordinate
- Keep QB windows **VISIBLE** — never minimize them during SDK operations
- Check `POPUP_HANDLING.md` for QuickBooks dialog handling procedures
