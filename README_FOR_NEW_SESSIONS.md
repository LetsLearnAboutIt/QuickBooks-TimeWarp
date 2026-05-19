# QB-TimeWarp — Quick Start for New Sessions

> **Read this FIRST when resuming work on this project in a new AI session or after a break.**

---

## 🔑 Top 5 Things to Do When Returning

### 1. Read the Project Context
```
Read these files in order:
1. PROJECT_CONTEXT.md   — What the project does and why
2. CHANGE_LOG.md        — What has been done so far
3. ARCHITECTURE.md      — How the code is structured
```

### 2. Connect to the Windows VM
```
RDP Address: aiagent.hostedremotedesktop.com:4420
Username:    VM-4420-11\AIAgent
Password:    01Hello02!@!
```
Verify QuickBooks 2023 and 2021 are running with company files open.

### 3. Check Git Status
```bash
cd /home/ubuntu/QB-TimeWarp
git status
git log --oneline -10
```
Review any uncommitted changes or recent commits.

### 4. Verify Configuration
```bash
cat /home/ubuntu/QB-TimeWarp/appsettings.json
```
Confirm:
- QB 2023 path: `C:\Program Files\Intuit\Quickbooks 2023\QBWPremierAccountant.exe`
- QB 2021 path: `C:\Program Files (x86)\Intuit\QuickBooks 2021`
- Company files point to correct locations
- Transformation rules are set as needed

### 5. Check What Needs Doing Next
- Review any TODO items or issues in the GitHub repo
- Check `TESTING_CHECKLIST.md` for untested items
- Ask the user what they need

---

## 📁 Key File Locations

### On Linux (Development)
| Path | Content |
|------|---------|
| `/home/ubuntu/QB-TimeWarp/` | Full source code |
| `/home/ubuntu/QB-TimeWarp/appsettings.json` | Main configuration |
| `/home/ubuntu/QB-TimeWarp/Configuration/FieldMappings.json` | Field mappings & format rules |
| `/home/ubuntu/QB-TimeWarp/Services/` | Core service classes |
| `/home/ubuntu/QB-TimeWarp/Models/` | Data models |
| `/home/ubuntu/QB-TimeWarp/Helpers/` | Utility functions |

### On Windows VM (Runtime)
| Path | Content |
|------|---------|
| `C:\QB-TimeWarp\` | Deployed application |
| `C:\Users\AIAgent\Desktop\AirMasters\Air-Masters-QB-2023.qbw` | QB 2023 company file (360 MB) |
| `C:\Users\Public\Documents\Intuit\QuickBooks\Company Files\Josh Safty 2021.qbw` | QB 2021 company file (13 MB) |

---

## 🔨 How to Build & Run

### Build on Linux
```bash
cd /home/ubuntu/QB-TimeWarp
dotnet publish -c Release -r win-x86 --self-contained true -o ./publish
```

### Deploy to Windows VM
Copy the `./publish/` folder contents to `C:\QB-TimeWarp\` on the VM.

### Run on Windows VM
```powershell
cd C:\QB-TimeWarp
.\QB-TimeWarp.exe
```

**Prerequisites**: QuickBooks must be running with company file open. Single-user mode recommended.

---

## 🌐 GitHub Repository

| Detail | Value |
|--------|-------|
| URL | https://github.com/LetsLearnAboutIt/QuickBooks-TimeWarp |
| Auth | Personal Access Token (PAT) |

```bash
# Push changes:
cd /home/ubuntu/QB-TimeWarp
git add -A
git commit -m "Description of changes"
git push origin main
```

---

## ⚙️ Key Configuration Quick Reference

### Transformation Rules (`appsettings.json`)
```json
"TransformationRules": {
  "ReactivateInactiveEntities": true,   // Inactive → Active
  "PreserveClassTracking": true,         // Keep class assignments
  "MatchAccountingModel": true           // Match Cash/Accrual
}
```

### Validation Settings (`appsettings.json`)
```json
"Validation": {
  "EnableFieldByFieldComparison": true,
  "EnableFinancialReconciliation": true,
  "EnableEntityCountVerification": true,
  "EnableJournalValidation": true,       // Debits = Credits check
  "ToleranceAmount": 0.01
}
```

### Format Rules (`Configuration/FieldMappings.json`)
All format preservation is enabled by default (dates, currency, phone, postal codes, memos).

---

## 🐛 Common Issues & Solutions

| Issue | Solution |
|-------|----------|
| **"COM object" error on Windows** | QuickBooks must be running. Ensure the build is x86, not x64. |
| **"Company file not found"** | Verify paths in `appsettings.json`. The company file must be open in QB. |
| **"Access denied" during import** | Switch QuickBooks to single-user mode. |
| **Build fails on Linux** | Expected — COM references are Windows-only. The build produces warnings but creates the output. Run on Windows VM. |
| **Git push fails** | PAT may have expired. Generate new PAT at github.com/settings/tokens. |
| **RDP connection refused** | VM may need to be restarted. Check with the hosting provider. |
| **Journal entries don't balance** | Check `formatRules.currencyDecimalPlaces` is set to 2. Review `TransformFunctions.PreserveCurrencyFormat()`. |
| **Dates have timezone errors** | Ensure `formatRules.stripTimezoneFromDates` is `true` in FieldMappings.json. |

---

## 📊 Where to Find Logs & Output

| Output | Location |
|--------|----------|
| Application logs | `.\Logs\QB-TimeWarp-{Date}.log` (on Windows VM) |
| Exported data | `.\ExportedData\*.json` |
| Export manifest | `.\ExportedData\manifest.json` |
| Schemas | `.\Schemas\*.json` |
| Validation report | `.\Validation\report.json` |
| Console output | Real-time during execution (color-coded) |

---

## 📖 Documentation Index

| Document | Purpose |
|----------|---------|
| `README.md` | Public-facing project description |
| `PROJECT_CONTEXT.md` | Full project context, requirements, design decisions |
| `SESSION_RESUME.md` | Connection details, credentials, paths |
| `CHANGE_LOG.md` | Chronological change history with reasoning |
| `ARCHITECTURE.md` | System architecture and service breakdown |
| `TESTING_CHECKLIST.md` | Step-by-step testing guide |
| `README_FOR_NEW_SESSIONS.md` | This file — quick start guide |
