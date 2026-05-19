# PATH_REFERENCE.md — Quick Reference for All File Paths

> **Last Updated**: May 19, 2026
> **IMPORTANT**: All folder and file names use **underscores instead of spaces** for Windows path compatibility.

---

## Why Underscores?

Spaces in Windows file paths cause issues with:
- Command-line tools that don't handle quoted paths correctly
- Batch scripts and PowerShell commands
- Git operations and SSH file transfers
- COM/SDK path resolution

Joseph renamed all files/folders on 2026-05-19 to remove spaces.

---

## Desktop Company Files (ORIGINALS — READ-ONLY)

### Testing Source (USE THIS FOR DEVELOPMENT)
| Detail | Value |
|--------|-------|
| **Folder** | `C:\Users\AIAgent\Desktop\Joshs_Gold_Coast\` |
| **File** | `Joshs_Gold_Coast_II_2023.qbw` |
| **Full Path** | `C:\Users\AIAgent\Desktop\Joshs_Gold_Coast\Joshs_Gold_Coast_II_2023.qbw` |
| **Size** | ~30 MB |
| **QB Version** | 2023 |
| **Purpose** | **Testing** — safe to use during development |
| **Password** | See `PASSWORDS.txt` |

### Production Source (DO NOT USE FOR TESTING)
| Detail | Value |
|--------|-------|
| **Folder** | `C:\Users\AIAgent\Desktop\Air_Masters\` |
| **File** | `Air_Masters_QB_2023.qbw` |
| **Full Path** | `C:\Users\AIAgent\Desktop\Air_Masters\Air_Masters_QB_2023.qbw` |
| **Size** | ~360 MB |
| **QB Version** | 2023 |
| **Purpose** | **Production** — real client data, DO NOT USE for testing |
| **Password** | `3825You171` |

### Target (QB 2021 Blank Template)
| Detail | Value |
|--------|-------|
| **Folder** | `C:\Users\AIAgent\Desktop\QB21_Blank_Template\` |
| **File** | `Blank_Template.qbw` |
| **Full Path** | `C:\Users\AIAgent\Desktop\QB21_Blank_Template\Blank_Template.qbw` |
| **QB Version** | 2021 |
| **Purpose** | **Target** — blank template that receives migrated data |
| **Password** | `Fl0640098!@!` |

---

## Working Copies (ALL OPERATIONS HAPPEN HERE)

| Directory | Source | Purpose |
|-----------|--------|---------|
| `C:\QB-TimeWarp\Working\Source\` | Copy of Joshs_Gold_Coast .qbw | Export reads from here |
| `C:\QB-TimeWarp\Working\Target\` | Copy of QB21_Blank_Template .qbw | Import writes to here |

Working copies are created automatically on first run. Use `--refresh` to re-copy from originals.

---

## Configuration (appsettings.json)

```json
{
  "SourceFiles": {
    "DesktopFolder": "C:\\Users\\AIAgent\\Desktop\\Joshs_Gold_Coast",
    "CompanyFileName": "*.qbw"
  },
  "TargetFiles": {
    "DesktopFolder": "C:\\Users\\AIAgent\\Desktop\\QB21_Blank_Template",
    "CompanyFileName": "*.qbw"
  },
  "WorkingDirectories": {
    "SourcePath": "C:\\QB-TimeWarp\\Working\\Source",
    "TargetPath": "C:\\QB-TimeWarp\\Working\\Target"
  }
}
```

---

## Old Names → New Names (Rename History)

| Old Name (with spaces) | New Name (underscores) |
|------------------------|----------------------|
| `Joshua's Gold Coast` | `Joshs_Gold_Coast` |
| `Blank Template` | `QB21_Blank_Template` |
| `Air Masters` | `Air_Masters` |
| `Air-Masters-QB-2023.qbw` | `Air_Masters_QB_2023.qbw` |

---

## Other Key Paths

| Path | Purpose |
|------|---------|
| `C:\QB-TimeWarp\` | Deployed application on Windows VM |
| `C:\QB-TimeWarp\Working\` | Working copies directory |
| `C:\QB-TimeWarp\ExportedData\` | Exported JSON data |
| `C:\QB-TimeWarp\Schemas\` | QB 2021 schemas |
| `C:\QB-TimeWarp\Logs\` | Application logs |
| `C:\QB-TimeWarp\Validation\` | Validation reports |
| `/home/ubuntu/QB-TimeWarp/` | Source code (Linux dev machine) |
