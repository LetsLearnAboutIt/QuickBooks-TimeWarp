# QuickBooks TimeWarp® — Agent Briefing & Session Notes

## ⚠️ CURRENT STATE — READ THIS FIRST ⚠️

**As of May 19, 2026 ~late session:**
- 🟡 **Staged import architecture COMPLETED** — 4-stage dependency-aware pipeline built
- 🟡 **Root cause analysis COMPLETED** — 5 root causes identified (see DEBUGGING_NOTES.MD)
- 🔴 **5 critical fixes identified, NOT YET IMPLEMENTED** — see CRITICAL_FIXES_NEEDED.MD
- 🟡 **Trial run showed 359 phantom successes** — success detection bug also identified
- **Next step**: Implement the 5 fixes in priority order, then re-run import

---

## 🚨 NOTEPAD PROTOCOL (CRITICAL — DO NOT SKIP) 🚨

**When Notepad appears on the VM screen, Joseph is talking to you.**

1. **STOP** everything immediately — do NOT minimize it and continue working
2. **READ** the message in Notepad carefully
3. **Commit to memory** — update debugging files with any new info
4. **Update documentation files** (this file, DEBUGGING_NOTES.MD, CHANGE_LOG.md)
5. **Come to chat** — respond to Joseph in the conversation

> Joseph uses Notepad as his intercom. Ignoring it = ignoring the user. This has caused wasted work in past sessions.

---

## 1. PROJECT IDENTITY

**QuickBooks TimeWarp®** — A Windows desktop tool that converts QuickBooks Desktop 2023 company files (.qbw) to QuickBooks Desktop 2021 format using the QBFC SDK, with ALL data intact.

---

## 2. PEOPLE

**Joseph** — The user. 40-year programmer, MSP owner. Full autonomy granted.
- **Notepad = Joseph's intercom.** See NOTEPAD PROTOCOL above.
- **His accountant** flagged issues: missing account numbers, reversed CC columns (FIXED), trial balance mismatch, CC balance inversion.

---

## 3. WINDOWS VM

### VM Performance Specs — THIS IS NOT A SLOW MACHINE
| Spec | Value |
|------|-------|
| **CPU** | AMD Ryzen 9 9950X (16-core / 32-thread) |
| **RAM** | 32 GB |
| **Network** | 2 Gbps fiber |
| **OS** | Windows (RDP accessible) |

> If something is slow, it's a code/architecture problem, NOT the machine. Don't blame the VM.

### CRITICAL: Reinstall tools at start of EVERY new context/session:
```bash
sudo apt-get install -y freerdp2-x11 sshpass
```

### SSH Helper Script (create at session start):
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

**Usage examples:**
```bash
/tmp/vm.sh "dir C:\QB-TimeWarp"                    # List files
/tmp/vm.sh "cd C:\QB-TimeWarp && git pull" 30       # Git pull (30s timeout)
/tmp/vm.sh "powershell -Command \"Get-Process\""    # PowerShell commands
/tmp/vm.sh "type C:\QB-TimeWarp\appsettings.json"   # Read a file
```

### SSH vs RDP — USE SSH FIRST, it's cheaper:
- SSH works for: git pull, file operations, checking processes, running builds, reading logs
- SSH tip: PowerShell commands need `powershell -Command "..."` wrapper
- SSH can be flaky — if a command returns empty, try again or use a simpler command

### RDP Connection (only when SSH won't work — GUI, launching QB, watching runs):
```bash
nohup xfreerdp /v:aiagent.hostedremotedesktop.com:4420 /u:'VM-4420-11\AIAgent' /p:'01Hello02!@!' /dynamic-resolution /cert:ignore /auto-reconnect +clipboard &>/tmp/rdp2.log & echo "RDP PID: $!" ; sleep 10 ; echo "Done"
```
> **NOTE:** `nohup` is required for proper taskbar visibility.

### VM Credentials:
- IP: `aiagent.hostedremotedesktop.com:4420` (RDP) / `aiagent.hostedremotedesktop.com` port 22 (SSH)
- User: `VM-4420-11\AIAgent`
- Pass: `01Hello02!@!`

### Key Paths on VM:
| Path | What |
|------|------|
| `C:\QB-TimeWarp\` | Deployed application |
| `C:\Users\AIAgent\dotnet\dotnet.exe` | .NET runtime |
| `C:\Users\AIAgent\Desktop\Joshs_Gold_Coast\Joshs_Gold_Coast_II_2023.qbw` | QB 2023 source (TESTING — 30MB) |
| `C:\Users\AIAgent\Desktop\Air_Masters\Air_Masters_QB_2023.qbw` | QB 2023 production (360MB — DO NOT USE for testing) |
| `C:\Users\AIAgent\Desktop\QB21_Blank_Template\Blank_Template.qbw` | QB 2021 target |
| `C:\Users\Public\Documents\Intuit\QuickBooks\Company Files\Josh Safty 2021.qbw` | QB 2021 company file (13 MB) |

### QuickBooks Installations:
- QB 2023 (64-bit): `C:\Program Files\Intuit\QuickBooks 2023\QBWPremierAccountant.exe` — process name: `qbw.exe`
- QB 2021 (32-bit): `C:\Program Files (x86)\Intuit\QuickBooks 2021\QBW32PremierAccountant.exe` — process name: `QBW32.exe`

---

## 4. CRITICAL TRANSFORMATION TIMING — ORDER MATTERS

```
┌─────────────────────────────────────────────────────────┐
│  1. Export data from QB 2023                            │
│  2. CLOSE QB 2023                                       │
│  3. OPEN QB 2021                                        │
│  4. THEN run transformation (2023 format → 2021 format) │
│  5. THEN import into QB 2021                            │
└─────────────────────────────────────────────────────────┘
```

**WHY this order**: The transformation needs to target the QB 2021 schema. QB 2021 must be the active/open instance during import. Having QB 2023 open simultaneously can cause COM conflicts and SDK version confusion.

---

## 5. WORKFLOW: Local → GitHub → VM

```
Linux (dev)          GitHub              Windows VM
┌──────────┐    ┌──────────────┐    ┌──────────────┐
│ Edit code │───▶│ git push     │───▶│ git pull     │
│ Build     │    │ origin main  │    │ Run/test     │
└──────────┘    └──────────────┘    └──────────────┘
```

```bash
# On Linux (after edits):
cd /home/ubuntu/QB-TimeWarp
git add -A && git commit -m "description" && git push origin main

# On VM (via SSH):
/tmp/vm.sh "cd C:\QB-TimeWarp && git pull" 30
```

---

## 6. CURRENT ROOT CAUSES (5 identified — see DEBUGGING_NOTES.MD)

1. **Hierarchical names** — Need to parse leaf name + set ParentRef
2. **IsActive field** — Must mark ALL entities active on import (even if inactive in 2023)
3. **Account types prerequisite** — Account types MUST exist before accounts
4. **Missing reference types** — Sales tax types, payment terms, customer types must be created BEFORE entities that reference them
5. **Field compatibility** — Must exclude fields that QB 2021 SDK 15.0 doesn't support
6. **(Bonus) Success detection bug** — 359 phantom successes counted incorrectly

See **CRITICAL_FIXES_NEEDED.MD** for implementation plan.

---

## 7. IMPORT ORDER OF OPERATIONS (CORRECT)

```
Stage 0 (Pre-Import):
  ├── Verify/create account types
  ├── Create missing reference types (sales tax codes, terms, customer types, etc.)
  └── Validate all prerequisites exist

Stage 1 (Foundation): Accounts (AFTER account types exist)
Stage 2 (Entities):   Customers, Vendors, Employees (AFTER accounts + reference types exist)
Stage 3 (Items):      All item subtypes
Stage 4 (Transactions): Invoices, Bills, Payments, etc.
```
