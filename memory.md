# 🧠 memory.md — Instant Session Resume for QB-TimeWarp

> **READ THIS ENTIRE FILE FIRST** before doing anything else.
> This is the single source of truth for resuming work with zero learning curve.
> Last Updated: 2026-05-19

---

## 🔴 CRITICAL REMINDERS (Read Before Every Action)

1. **PROJECT vs SOLUTION**: Always build with the `.csproj` file, NEVER a `.sln`.
   ```bash
   /home/ubuntu/.dotnet/dotnet build    # ✅ Correct — uses QB-TimeWarp.csproj
   dotnet build                          # ❌ FAILS — dotnet not in PATH
   dotnet build QB-TimeWarp.sln          # ❌ FAILS — no .sln file exists
   ```

2. **dotnet is NOT in PATH**: You MUST use the full path:
   ```bash
   /home/ubuntu/.dotnet/dotnet build
   /home/ubuntu/.dotnet/dotnet publish -c Release -r win-x86 --self-contained true -o ./publish
   ```

3. **Build target**: `net6.0-windows` / `win-x86` — builds on Linux, runs only on Windows VM.

4. **Expected build warning**: `CS8602` in `QBConnectionManager.cs` line 385 is a known nullable reference warning. **0 Errors, 1 Warning = GOOD BUILD.**

5. **Air Masters = PRODUCTION DATA (360MB)** — NEVER use for testing. Use `Joshs_Gold_Coast` (30MB) instead.

---

## 📋 CURRENT STATUS (as of 2026-05-19)

### What's Done
- ✅ All 5 critical fixes implemented (commit `d0c9ffd`)
- ✅ File/folder rename: spaces → underscores (commit `1189db1`)
- ✅ PATH_REFERENCE.md fixed with complete `.qbw` paths (commit `f8c3d51`)
- ✅ All documentation updated for underscore naming
- ✅ Staged import with dependency analysis wired up
- ✅ Format preservation (dates, currency, phone, postal codes, memos)
- ✅ SDK version compatibility (QB 2023 SDK 16.0 → QB 2021 SDK 15.0)
- ✅ Safety system: originals never touched, working copies only

### What's Next
1. **Create working copies** on Windows VM (`--refresh` flag)
2. **Run first migration test** using Joshs_Gold_Coast → QB21_Blank_Template
3. **Monitor for QB popups** during SDK operations (see `POPUP_HANDLING.md`)
4. **Validate results** — check exported JSON, import counts, financial reconciliation
5. **Deploy to production** (Air Masters) only after testing passes

### The 6 Fixes Ready to Deploy
| # | Fix | File(s) |
|---|-----|---------|
| 1 | Hierarchical name parsing (colon-separated → Name + ParentRef) | DataTransformer.cs, DataImporter.cs |
| 2 | Force IsActive=true for all entities | DataTransformer.cs, DataImporter.cs |
| 3 | Stage 0 — Account type verification | DataImporter.cs |
| 4 | Create missing reference types (SalesTaxCodes, Terms, etc.) | DataImporter.cs |
| 5 | Field compatibility — skip SDK 16.0-only fields | DataImporter.cs, QBSDKVersionHelper.cs |
| 6 | Success detection bug fix (don't overwrite true with false) | DataImporter.cs |

---

## 🔑 ALL PASSWORDS & CREDENTIALS

### Windows VM (RDP)
| Detail | Value |
|--------|-------|
| **Address** | `aiagent.hostedremotedesktop.com:4420` |
| **Username** | `VM-4420-11\AIAgent` |
| **Password** | `01Hello02!@!` |

### RDP Connection Command
```bash
xfreerdp /v:aiagent.hostedremotedesktop.com:4420 /u:'VM-4420-11\AIAgent' /p:'01Hello02!@!' /dynamic-resolution /cert:ignore /auto-reconnect +clipboard &>/tmp/rdp2.log & echo "RDP PID: $!" ; sleep 10 ; echo "Done"
```

### VPN (if needed)
> No separate VPN is required — RDP connects directly to `aiagent.hostedremotedesktop.com:4420`.

### QuickBooks Company File Passwords
| File | Password |
|------|----------|
| `Joshs_Gold_Coast_II_2023.qbw` | See `PASSWORDS.txt` in project root |
| `Air_Masters_QB_2023.qbw` | `3825You171` |
| `Josh Safty 2021.qbw` | `3825You171` |
| `Blank_Template.qbw` | `Fl0640098!@!` |

### GitHub
| Detail | Value |
|--------|-------|
| **Repo** | `https://github.com/LetsLearnAboutIt/QuickBooks-TimeWarp` |
| **Auth** | PAT embedded in git remote (auto-configured) |
| **Push** | `git push origin main` (just works) |
| **If PAT expires** | Regenerate at https://github.com/settings/tokens with `repo` scope |

---

## 📂 ALL FILE PATHS

### Desktop Sources (ORIGINALS — READ-ONLY)
| Purpose | Full Path | Size |
|---------|-----------|------|
| **Testing Source** | `C:\Users\AIAgent\Desktop\Joshs_Gold_Coast\Joshs_Gold_Coast_II_2023.qbw` | ~30 MB |
| **Production** (DO NOT USE) | `C:\Users\AIAgent\Desktop\Air_Masters\Air_Masters_QB_2023.qbw` | ~360 MB |
| **Target Template** | `C:\Users\AIAgent\Desktop\QB21_Blank_Template\Blank_Template.qbw` | — |
| **Other QB21 File** | `C:\Users\Public\Documents\Intuit\QuickBooks\Company Files\Josh Safty 2021.qbw` | ~13 MB |

### Working Directories (Operations Happen Here)
| Path | Content |
|------|---------|
| `C:\QB-TimeWarp\Working\Source\` | Copy of Joshs_Gold_Coast .qbw |
| `C:\QB-TimeWarp\Working\Target\` | Copy of QB21_Blank_Template .qbw |

### QuickBooks Installations
| Version | Path |
|---------|------|
| QB 2023 Premier Accountant | `C:\Program Files\Intuit\Quickbooks 2023\QBWPremierAccountant.exe` |
| QB 2021 | `C:\Program Files (x86)\Intuit\QuickBooks 2021` |

### Linux Development
| Path | Content |
|------|---------|
| `/home/ubuntu/QB-TimeWarp/` | Project root (source code) |
| `/home/ubuntu/.dotnet/dotnet` | .NET SDK (NOT in PATH!) |

### Output on Windows VM
| Path | Content |
|------|---------|
| `C:\QB-TimeWarp\` | Deployed application |
| `C:\QB-TimeWarp\ExportedData\` | Exported JSON from QB 2023 |
| `C:\QB-TimeWarp\Schemas\` | QB 2021 field schemas |
| `C:\QB-TimeWarp\Logs\` | Application logs (Serilog) |
| `C:\QB-TimeWarp\Validation\` | Validation reports |

---

## 🔧 DIAGNOSTIC COMMANDS

### Verify Build
```bash
cd /home/ubuntu/QB-TimeWarp
/home/ubuntu/.dotnet/dotnet build
# Expected: 0 Errors, 1 Warning (CS8602)
```

### Check Git State
```bash
cd /home/ubuntu/QB-TimeWarp
git status
git log --oneline -10
git remote -v
```

### Verify RDP Connection
```bash
# Test if RDP port is reachable:
nc -zv aiagent.hostedremotedesktop.com 4420
# Or connect:
xfreerdp /v:aiagent.hostedremotedesktop.com:4420 /u:'VM-4420-11\AIAgent' /p:'01Hello02!@!' /dynamic-resolution /cert:ignore /auto-reconnect +clipboard &>/tmp/rdp2.log & echo "RDP PID: $!" ; sleep 10 ; echo "Done"
```

### Deploy to Windows VM
```bash
cd /home/ubuntu/QB-TimeWarp
/home/ubuntu/.dotnet/dotnet publish -c Release -r win-x86 --self-contained true -o ./publish
# Then copy ./publish/ to C:\QB-TimeWarp\ on the VM via RDP file transfer
```

---

## 📖 FILES TO READ ON STARTUP

Read these in order for full context:

### Essential (read every session)
1. **`memory.md`** ← You are here
2. **`PROJECT_INSTRUCTIONS.md`** — Primary task instructions
3. **`PROJECT_CONTEXT.md`** — What the project does and why
4. **`CHANGE_LOG.md`** — Complete history of all changes

### Supplemental (read as needed)
5. **`PATH_REFERENCE.md`** — All file paths with old→new rename mapping
6. **`ARCHITECTURE.md`** — Code structure and service breakdown
7. **`TESTING_CHECKLIST.md`** — Step-by-step test procedures
8. **`POPUP_HANDLING.md`** — QuickBooks popup responses
9. **`SAFETY_FEATURES.md`** — Multi-layer protection system
10. **`SESSION_RESUME.md`** — Connection details (duplicated here for redundancy)
11. **`README_FOR_NEW_SESSIONS.md`** — Quick-start guide
12. **`PASSWORDS.txt`** — All credentials (gitignored, never committed)

### Supplemental Files (in /home/ubuntu/, NOT in project dir)

These files live in `/home/ubuntu/` (the home directory root), **not** inside the QB-TimeWarp project folder:

| File | Location | Purpose |
|------|----------|---------|
| **`PROJECT_INSTRUCTIONS_ADDITION.md`** | `/home/ubuntu/` | Root cause analysis: 2 bugs found, import regression details, QBXML requirements |
| **`DEBUGGING_NOTES.md`** | `/home/ubuntu/` | Deep debugging analysis: success detection bug, QBXML parsing errors, code flow |
| **`memory.md`** (old version) | `/home/ubuntu/` | Earlier session memory (superseded by this file in the project) |

> ⚠️ `.docx` and `.pdf` versions also exist alongside each `.md` file.

When resuming, always check for these:
```bash
ls /home/ubuntu/PROJECT_INSTRUCTIONS_ADDITION.md 2>/dev/null
ls /home/ubuntu/DEBUGGING_NOTES.md 2>/dev/null
ls /home/ubuntu/memory.md 2>/dev/null
```

### Legacy File in Project Dir
| File | Location | Notes |
|------|----------|-------|
| **`PROJECT_INSTRUCTIONS.MD`** (uppercase) | `/home/ubuntu/QB-TimeWarp/` | Original briefing from earlier session — still valid context |
| **`DEBUGGING_NOTES.MD`** (uppercase) | `/home/ubuntu/QB-TimeWarp/` | Copy of debugging notes in project dir |

---

## 👤 ABOUT JOSEPH (The User)

- **40-year programmer**, MSP (Managed Service Provider) owner
- Wants **precise, complete documentation** — no shortcuts
- Prefers **collaborative workflow** — he watches the VM screen and programs alongside AI
- Renamed all Desktop files/folders from spaces → underscores on 2026-05-19
- Strong opinions about path safety — originals must NEVER be modified
- Understands C#/.NET deeply — explain reasoning, not basics

---

## ⚡ QUICK-START CHECKLIST FOR NEW SESSIONS

```
□ Read memory.md (this file)
□ Read PROJECT_INSTRUCTIONS.md
□ Check for supplemental files (see list above)
□ git status / git log --oneline -10
□ /home/ubuntu/.dotnet/dotnet build (verify 0 errors)
□ Connect to Windows VM via RDP (if needed for testing)
□ Verify QB is running with company files open (if testing)
□ Check with Joseph on what to do next
```
