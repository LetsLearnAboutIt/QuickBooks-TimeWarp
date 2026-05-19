# 🛡️ Safety Features — Original File Protection

## Overview

QB-TimeWarp implements **multiple layers of protection** to ensure that original QuickBooks company files on the Desktop are **NEVER modified**. All migration operations work exclusively with copies in dedicated Working directories.

## Folder Structure

```
Desktop (READ-ONLY originals — NEVER touched):
├── Joshua's Gold Coast\       ← QB 2023 source (30MB, testing file)
│   └── *.qbw
├── Blank Template\            ← QB 2021 target
│   └── *.qbw
└── Air Masters\               ← QB 2023 production (360MB — DO NOT USE)
    └── Air-Masters-QB-2023.qbw

C:\QB-TimeWarp\ (Application workspace):
├── Working\
│   ├── Source\                ← COPY of Joshua's Gold Coast .qbw
│   └── Target\                ← COPY of Blank Template .qbw
├── ExportedData\
├── Schemas\
├── Logs\
└── Validation\
```

## Safety Layers

### Layer 1: WorkingDirectoryManager (Pre-Operation)
- **When**: Before ANY QuickBooks operations begin
- **What**: Copies original files from Desktop to `Working\Source\` and `Working\Target\`
- **Verification**: Compares file sizes after copy to ensure integrity
- **Skip logic**: Won't overwrite existing working copies unless `--refresh` is used
- **Service**: `Services/WorkingDirectoryManager.cs`

### Layer 2: Configuration Path Override (Program.cs)
- **When**: After working copies are created, before any service initialization
- **What**: Overwrites `QuickBooks.QB2023.CompanyFilePath` and `QuickBooks.QB2021.CompanyFilePath` to point to Working directory copies
- **Result**: All downstream services automatically receive working copy paths

### Layer 3: QBConnectionManager Validation (Every Connection)
- **When**: Every time a QB connection is created AND every time `.Connect()` is called
- **What**: Checks the company file path against a list of protected Desktop folder patterns
- **Action**: Throws `OriginalFileProtectionException` if a protected path is detected
- **Protected patterns**:
  - `\Desktop\Joshua's Gold Coast`
  - `\Desktop\Blank Template`
  - `\Desktop\Air Masters`
  - `\Desktop\AirMasters`

### Layer 4: Desktop Path Rejection (Program.cs Fallback)
- **When**: If `AutoCreateWorkingCopies` is disabled
- **What**: Validates that configured `CompanyFilePath` values don't point to Desktop folders
- **Action**: Throws `OriginalFileProtectionException` with clear instructions

### Layer 5: Working Copy Validation Helper
- **What**: `WorkingDirectoryManager.ValidateNotOriginalPath()` method
- **Usage**: Can be called before any write/modify operation to verify the target path
- **Checks**: Against configured Desktop source/target folders AND general Desktop path patterns

## Configuration

In `appsettings.json`:

```json
{
  "SourceFiles": {
    "DesktopFolder": "C:\\Users\\AIAgent\\Desktop\\Joshua's Gold Coast",
    "CompanyFileName": "*.qbw"
  },
  "TargetFiles": {
    "DesktopFolder": "C:\\Users\\AIAgent\\Desktop\\Blank Template",
    "CompanyFileName": "*.qbw"
  },
  "WorkingDirectories": {
    "SourcePath": "C:\\QB-TimeWarp\\Working\\Source",
    "TargetPath": "C:\\QB-TimeWarp\\Working\\Target",
    "AutoCreateWorkingCopies": true,
    "PreserveOriginals": true
  }
}
```

## Command-Line Options

| Option | Description |
|--------|-------------|
| `--refresh` | Force re-copy originals to Working directories (deletes existing copies first) |
| `--cleanup` | Remove all Working directories and exit (originals remain untouched) |

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | General error |
| 2 | Working copy creation/validation failure |
| 99 | **SAFETY VIOLATION** — attempted operation on original file |

## What Happens If...

### ...working copies already exist?
- They are reused without re-copying (faster startup)
- Use `--refresh` to force a fresh copy from originals

### ...an original file path is accidentally configured?
- `QBConnectionManager` blocks the connection attempt
- `OriginalFileProtectionException` is thrown
- Application exits with code 99
- Error message clearly identifies which file and why

### ...the Desktop folders don't exist?
- `WorkingDirectoryManager` throws `WorkingCopyException`
- Application exits with code 2
- Error message identifies which folder is missing

### ...the .qbw file can't be found?
- Glob pattern `*.qbw` is used to find the company file
- If no match, a clear error identifies the folder and pattern
- If multiple matches, the first is used with a warning

## Logging

At startup, you'll see prominent log messages:
```
╔══════════════════════════════════════════════════════════════╗
║  WORKING WITH COPIES — ORIGINALS PRESERVED                  ║
╠══════════════════════════════════════════════════════════════╣
║  Source (QB 2023): C:\QB-TimeWarp\Working\Source\*.qbw       ║
║  Target (QB 2021): C:\QB-TimeWarp\Working\Target\*.qbw       ║
╚══════════════════════════════════════════════════════════════╝
```

And for each QBConnectionManager:
```
[QB2023-Export] ✓ Using working copy (originals protected): C:\QB-TimeWarp\Working\Source\*.qbw
```
