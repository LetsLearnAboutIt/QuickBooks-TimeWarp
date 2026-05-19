# QB-TimeWarp — Session Resume Guide

> **Purpose**: This document contains all connection details, credentials, and paths needed to resume work on this project in a new session.

---

## ⚠️ CRITICAL OPERATIONAL DETAILS — READ FIRST ⚠️

### QuickBooks Company File Passwords

| Company File | Password |
|-------------|----------|
| **Air-Masters-QB-2023.qbw** | `3825You171` |
| **Josh Safty 2021.qbw** | `3825You171` |
| **Blank Template** | `Fl0640098!@!` |

> **Enter these passwords when QuickBooks prompts on opening company files.**

### ⚡ Operational Rules — ALWAYS FOLLOW

1. **Keep QB windows VISIBLE during execution** — Do NOT minimize QuickBooks. Popups will appear that need monitoring and handling.
2. **This is COLLABORATIVE** — The user is actively watching the VM screen and programming alongside the AI. Check in with the user regularly — they can see things automation misses.
3. **Don't barrel through** — Pause at key stages and coordinate with the user before proceeding.
4. **Monitor for popups** — QuickBooks generates permission dialogs, multi-user warnings, and other popups that can block SDK operations silently. See `POPUP_HANDLING.md` for details.
5. **Passwords file** — All credentials are also stored in `PASSWORDS.txt` in the project root for quick reference.

---

## 1. Windows VM Connection

| Detail | Value |
|--------|-------|
| **RDP Address** | `aiagent.hostedremotedesktop.com:4420` |
| **Username** | `VM-4420-11\AIAgent` |
| **Password** | `01Hello02!@!` |
| **Network** | 2 Gbps fiber connection |
| **OS** | Windows (with QuickBooks 2023 & 2021 installed) |

### How to Connect via RDP
```bash
# From Linux/Mac terminal:
xfreerdp /v:aiagent.hostedremotedesktop.com:4420 /u:'VM-4420-11\AIAgent' /p:'01Hello02!@!' /w:1920 /h:1080

# Or use any RDP client (Remmina, Microsoft Remote Desktop, etc.)
```

---

## 2. GitHub Repository

| Detail | Value |
|--------|-------|
| **Repository URL** | https://github.com/LetsLearnAboutIt/QuickBooks-TimeWarp |
| **Authentication** | Personal Access Token (PAT) |

### How to Authenticate with GitHub
```bash
# Clone with PAT embedded in URL:
git clone https://<PAT_TOKEN>@github.com/LetsLearnAboutIt/QuickBooks-TimeWarp.git

# Or configure credential helper:
git config --global credential.helper store
# Then on first push/pull, enter username + PAT as password

# Or set remote with token:
git remote set-url origin https://<PAT_TOKEN>@github.com/LetsLearnAboutIt/QuickBooks-TimeWarp.git
```

> **Note**: PAT tokens expire. If authentication fails, generate a new PAT at https://github.com/settings/tokens with `repo` scope.

---

## 3. QuickBooks Installation Paths

| Application | Path |
|------------|------|
| **QB 2023 Premier Accountant** | `C:\Program Files\Intuit\Quickbooks 2023\QBWPremierAccountant.exe` |
| **QB 2021** | `C:\Program Files (x86)\Intuit\QuickBooks 2021` |

### Company File Paths

| File | Path | Size |
|------|------|------|
| **QB 2023 Company File** | `C:\Users\AIAgent\Desktop\AirMasters\Air-Masters-QB-2023.qbw` | ~360 MB |
| **QB 2021 Company File** | `C:\Users\Public\Documents\Intuit\QuickBooks\Company Files\Josh Safty 2021.qbw` | ~13 MB |

---

## 4. Application Deployment

| Detail | Value |
|--------|-------|
| **Deployment Location** | `C:\QB-TimeWarp` on the Windows VM |
| **Project Framework** | .NET 6.0, x86, Windows-only |
| **Source Code (Linux)** | `/home/ubuntu/QB-TimeWarp/` |

### How to Build and Deploy

```bash
# 1. On Linux (development machine), build the project:
cd /home/ubuntu/QB-TimeWarp
dotnet publish -c Release -r win-x86 --self-contained true -o ./publish

# 2. Copy published output to Windows VM:
scp -r ./publish/* AIAgent@aiagent.hostedremotedesktop.com:C:/QB-TimeWarp/
# (Or use RDP file transfer / shared folder)

# 3. On the Windows VM, run:
cd C:\QB-TimeWarp
QB-TimeWarp.exe
```

### Alternative: Build on Windows VM
```powershell
# On the Windows VM (if .NET SDK is installed):
cd C:\QB-TimeWarp
dotnet build -c Release
dotnet run
```

### Important Build Notes
- **Must target x86**: The QBXML SDK is 32-bit only
- **QuickBooks must be running**: The SDK connects to a running QB instance via COM
- **Company file must be open**: Open the company file in QB before running the tool
- **Single-user mode recommended**: For reliable SDK access during migration

---

## 5. Configuration Files

| File | Location | Purpose |
|------|----------|---------|
| `appsettings.json` | Project root | Main config: QB paths, export/import settings, transformation rules |
| `Configuration/FieldMappings.json` | Configuration folder | Field mappings, format rules, data type constraints |

### Key Configuration Toggles
```json
{
  "TransformationRules": {
    "ReactivateInactiveEntities": true,   // Convert inactive → active
    "PreserveClassTracking": true,         // Maintain class assignments
    "MatchAccountingModel": true           // Match Cash/Accrual basis
  }
}
```

---

## 6. Output Directories

| Directory | Purpose |
|-----------|---------|
| `.\ExportedData\` | Exported JSON files from QB 2023 |
| `.\Schemas\` | Extracted QB 2021 field schemas |
| `.\Logs\` | Application logs (Serilog) |
| `.\Validation\` | Validation reports (JSON format) |

---

## 7. Network & Environment Notes

- The Windows VM has a **2 Gbps fiber connection** — file transfers and SDK operations are fast
- QuickBooks SDK operations are **single-threaded** due to COM constraints
- Large company files (360 MB) may take significant time to fully export
- The VM may have RDP session limits — reconnect if disconnected
