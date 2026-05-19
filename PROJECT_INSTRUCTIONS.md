# PROJECT_INSTRUCTIONS.md — QB-TimeWarp

---

## 🔥 READ FIRST: `/home/ubuntu/QB-TimeWarp/memory.md`

> **Before doing ANYTHING else**, read `memory.md` in this directory.
> It contains all passwords, connection details, paths, current status,
> and the complete quick-start checklist for immediate session resumption.

---

## 📚 ALSO READ THESE SUPPLEMENTAL FILES

Before starting work, check for and read these files if they exist:

### Always Read
| File | Purpose |
|------|---------|
| `memory.md` | 🧠 Complete session memory — passwords, paths, status, next steps |
| `PROJECT_CONTEXT.md` | What the project does, design decisions, requirements |
| `CHANGE_LOG.md` | Full chronological history of every change made |

### Supplemental Files (in /home/ubuntu/, NOT the project dir!)
```bash
# Run this EVERY session to read supplemental files:
cat /home/ubuntu/PROJECT_INSTRUCTIONS_ADDITION.md 2>/dev/null
cat /home/ubuntu/DEBUGGING_NOTES.md 2>/dev/null
```

| File | Location | Purpose |
|------|----------|---------|
| **`PROJECT_INSTRUCTIONS_ADDITION.md`** | `/home/ubuntu/` | Root cause analysis of import regression — 2 bugs, QBXML requirements, fix priorities |
| **`DEBUGGING_NOTES.md`** | `/home/ubuntu/` | Deep debugging: success detection bug, QBXML parsing errors, code architecture notes |

### Legacy Files in Project Dir (still valid context)
| File | Purpose |
|------|---------|
| **`PROJECT_INSTRUCTIONS.MD`** (uppercase) | Original agent briefing from earlier session |
| **`DEBUGGING_NOTES.MD`** (uppercase) | Debugging notes copy in project dir |

### Reference As Needed
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
