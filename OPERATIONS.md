# QB-TimeWarp Operations Guide

## 🔥 FIRST: Access Project Memory

Before doing ANYTHING else, connect to MCP memory to load all project context:

```bash
# MCP memory is already configured - just reference it
# All QB-TimeWarp credentials, paths, and history are stored in entity: QB-TimeWarp-Operations
```

The MCP memory contains:
- VM credentials (RDP/SSH)
- QuickBooks file locations and passwords
- GitHub repo info and SSH keys
- Complete migration history and bug fixes
- All previous conversation context

---

> **Purpose**: Complete operational procedures for accessing the Windows VM, running migrations, and retrieving diagnostics. This document is the single source of truth for all VM access, restart, and troubleshooting procedures.
>
> **Audience**: AI agents and developers resuming work on QB-TimeWarp.
>
> **Last Updated**: 2026-05-24

---

## 1. Linux VM Environment Setup

These commands must be run **at the start of every new session** — tools do not persist across sessions:

```bash
sudo apt-get install -y freerdp2-x11 sshpass
```

| Package | Purpose |
|---------|---------|
| `freerdp2-x11` | RDP client for connecting to the Windows VM |
| `sshpass` | Non-interactive SSH password authentication |

---

## 2. VM Credentials

| Property | Value |
|----------|-------|
| **Hostname** | `aiagent.hostedremotedesktop.com` |
| **RDP Port** | `4420` |
| **SSH Port** | `22` |
| **Username** | `VM-4420-11\AIAgent` |
| **Password** | `01Hello02!@!` |

---

## 3. SSH Helper Script

Create this script at session start for remote command execution with timeout handling.
SSH commands that interact with QuickBooks or long-running processes may hang — the timeout prevents blocking.

```bash
cat > /tmp/vm.sh << 'EOF'
#!/bin/bash
CMD="$1"
TIMEOUT="${2:-15}"
sshpass -p '01Hello02!@!' ssh -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null 'VM-4420-11\AIAgent'@aiagent.hostedremotedesktop.com "$CMD" > /tmp/ssh_out.txt 2>/dev/null &
PID=$!
sleep $TIMEOUT
kill $PID 2>/dev/null
wait $PID 2>/dev/null
cat /tmp/ssh_out.txt
EOF
chmod +x /tmp/vm.sh
```

**Usage:**
```bash
# Default 15-second timeout
/tmp/vm.sh "dir C:\\QB-TimeWarp"

# Custom 30-second timeout
/tmp/vm.sh "dotnet build -c Release" 30

# PowerShell commands must be wrapped
/tmp/vm.sh 'powershell -Command "Get-Service QuickBooksDB33"'
```

> **Tip**: Use SSH first for git and file operations. Wrap PowerShell commands in `powershell -Command "..."`.

---

## 4. RDP Connection

### Connect

```bash
pkill -f xfreerdp 2>/dev/null; sleep 1
xfreerdp /v:aiagent.hostedremotedesktop.com:4420 /u:'VM-4420-11\AIAgent' /p:'01Hello02!@!' /dynamic-resolution /cert:ignore /auto-reconnect +clipboard &>/tmp/rdp2.log &
echo "RDP PID: $!"
sleep 10
echo "Done"
```

### Reconnection Notes

- Desktop state and Admin PowerShell windows **persist across reconnects**
- QuickBooks windows may be hidden behind other windows — use `Alt+Tab` to find them
- If RDP disconnects mid-migration, the migration continues running on the VM
- Reconnect with the same command above to resume viewing

---

## 5. Complete Migration Restart Script

Run this in an **Admin PowerShell** on the Windows VM to pull the latest code and execute a fresh migration:

```powershell
cd C:\QB-TimeWarp

# ── Step 1: Configure SSH (use private deploy key) ──────────────
git config core.sshCommand "ssh -i C:/Users/AIAgent/Desktop/Certs/AIAgent-Github"

# ── Step 2: Change remote to SSH ────────────────────────────────
git remote set-url origin git@github.com:LetsLearnAboutIt/QuickBooks-TimeWarp.git

# ── Step 3: Pull latest code ────────────────────────────────────
git pull origin main

# ── Step 4: Clean rebuild (safer version) ───────────────────────
dotnet clean -c Release
dotnet build -c Release

# ── Step 5: Copy config to output ───────────────────────────────
copy appsettings.json bin\x86\Release\net6.0-windows\win-x86\

# ── Step 6: Cleanup — kill QB processes, reset target file ──────
taskkill /F /IM QBW32.exe 2>$null; taskkill /F /IM qbw.exe 2>$null
Stop-Service QuickBooksDB33 -Force -ErrorAction SilentlyContinue; Start-Sleep 3
Remove-Item 'C:\QB-TimeWarp\Working\Target\Blank_Template.qbw' -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item 'C:\QB-TimeWarp\Working\QB21_Blank_Template\Blank_Template.qbw' 'C:\QB-TimeWarp\Working\Target\Blank_Template.qbw' -Force
Start-Service QuickBooksDB33

# ── Step 7: Run migration ──────────────────────────────────────
cd bin\x86\Release\net6.0-windows\win-x86
.\QB-TimeWarp.exe
```

### Important Notes

- **QuickBooks popups**: The migration may trigger QB password prompts or update dialogs. See `POPUP_HANDLING.md` for handling procedures.
- **Working copies**: Step 6 resets only the target file. The source file persists between runs (read-only operations). Use `--refresh` flag if a full source reset is needed.
- **Build output**: The x86 Release build targets `net6.0-windows` and outputs to `bin\x86\Release\net6.0-windows\win-x86\`.

---

## 6. After-Run Diagnostics

### Transfer Folder

All diagnostic files (logs, JSON reports) are written to:

```
C:\QB-TimeWarp\Diagnostics\
```

### Retrieving Diagnostics via SSH

```bash
# List available diagnostic files
/tmp/vm.sh "dir C:\\QB-TimeWarp\\Diagnostics"

# Copy a specific file to Linux VM via SCP
sshpass -p '01Hello02!@!' scp -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null \
  'VM-4420-11\AIAgent'@aiagent.hostedremotedesktop.com:'C:/QB-TimeWarp/Diagnostics/MigrationReport_*.json' \
  /home/ubuntu/Uploads/
```

### Key Diagnostic Files

| File Pattern | Contents |
|-------------|----------|
| `MigrationReport_*.json` | Full import results — success/fail counts per entity type |
| `QB-TimeWarp-*.log` | Serilog output — detailed operation log with timestamps |
| `ValidationReport_*.json` | Post-migration validation — balance reconciliation results |

---

## 7. QuickBooks Installations

| Version | Architecture | Executable Path | Process Name |
|---------|-------------|-----------------|-------------|
| **QB 2023** | 64-bit | `C:\Program Files\Intuit\QuickBooks 2023\QBWPremierAccountant.exe` | `qbw.exe` |
| **QB 2021** | 32-bit | `C:\Program Files (x86)\Intuit\QuickBooks 2021\QBW32PremierAccountant.exe` | `QBW32.exe` |

### Key Service

| Service | Purpose |
|---------|---------|
| `QuickBooksDB33` | QuickBooks Database Server Manager — must be running for SDK access |

```powershell
# Check status
Get-Service QuickBooksDB33

# Restart
Stop-Service QuickBooksDB33 -Force; Start-Sleep 3; Start-Service QuickBooksDB33
```

---

## 8. QuickBooks File Passwords

| Company File | Password |
|-------------|----------|
| `Joshs_Gold_Coast_II_23.qbw` (QB 2023 test source) | `3825You171` |
| `Air_Master_QB_2023.qbw` (QB 2023 production — DO NOT USE for testing) | `3825You171` |
| `Blank_Template.qbw` (QB 2021 target) | `Fl0640098!@!` |

> ⚠️ **Security**: These passwords are also stored in `PASSWORDS.txt` (git-ignored). Never commit credentials to the repository.

---

## 9. GitHub Repository

| Property | Value |
|----------|-------|
| **URL** | `https://github.com/LetsLearnAboutIt/QuickBooks-TimeWarp` |
| **SSH URL** | `git@github.com:LetsLearnAboutIt/QuickBooks-TimeWarp.git` |
| **Deploy Key (Public)** | `ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIOW7ctkxQiDeC8wEJkkzwbXRSbQ3verpvN2zTLCxF6rc joseph@qb-timewarp` |
| **Deploy Key Location (VM)** | `C:\Users\AIAgent\Desktop\Certs\AIAgent-Github` |

### Git Configuration on Windows VM

```powershell
# Configure SSH key for git operations
git config core.sshCommand "ssh -i C:/Users/AIAgent/Desktop/Certs/AIAgent-Github"

# Set remote to SSH (required for deploy key auth)
git remote set-url origin git@github.com:LetsLearnAboutIt/QuickBooks-TimeWarp.git
```

### From Linux VM (uses HTTPS + GitHub App token)

```bash
cd /home/ubuntu/QB-TimeWarp
git push origin main
```

---

## Quick Reference — Full Session Startup Sequence

```bash
# 1. Install tools
sudo apt-get install -y freerdp2-x11 sshpass

# 2. Create SSH helper
cat > /tmp/vm.sh << 'SCRIPT'
#!/bin/bash
CMD="$1"; TIMEOUT="${2:-15}"
sshpass -p '01Hello02!@!' ssh -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null 'VM-4420-11\AIAgent'@aiagent.hostedremotedesktop.com "$CMD" > /tmp/ssh_out.txt 2>/dev/null &
PID=$!; sleep $TIMEOUT; kill $PID 2>/dev/null; wait $PID 2>/dev/null; cat /tmp/ssh_out.txt
SCRIPT
chmod +x /tmp/vm.sh

# 3. Connect RDP
pkill -f xfreerdp 2>/dev/null; sleep 1
xfreerdp /v:aiagent.hostedremotedesktop.com:4420 /u:'VM-4420-11\AIAgent' /p:'01Hello02!@!' /dynamic-resolution /cert:ignore /auto-reconnect +clipboard &>/tmp/rdp2.log &
sleep 10

# 4. Verify VM is accessible
/tmp/vm.sh "hostname"
```
