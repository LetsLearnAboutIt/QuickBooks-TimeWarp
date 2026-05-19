# QuickBooks TimeWarp® — Agent Briefing & Session Notes

## ⚠️ CURRENT STATE — READ THIS FIRST ⚠️


---

## 1. PROJECT IDENTITY

**QuickBooks TimeWarp®** — A Windows desktop tool that converts QuickBooks Desktop 2023 company files (.qbw) to QuickBooks Desktop 2021 format using the QBFC SDK, with ALL data intact.


---

## 2. PEOPLE

**Joseph** — The user. 40-year programmer, MSP owner. Full autonomy granted.
- **Notepad = Joseph's intercom.** When Notepad appears on the VM screen, STOP everything and READ it. Joseph is communicating with you.
- **His accountant** flagged issues: missing account numbers, reversed CC columns (FIXED), trial balance mismatch, CC balance inversion.

---

## 3. WINDOWS VM

**CRITICAL: MUST reinstall tools at start of EVERY new context/session:**
```bash
sudo apt-get install -y freerdp2-x11 sshpass
```

**SSH Helper Script (create at session start):**
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

**SSH vs RDP — USE SSH FIRST, it's cheaper:**
- SSH works for: git pull, file operations, checking processes, running Python one-liners, service 
- SSH tip: PowerShell commands need `powershell -Command "..."` wrapper
- SSH can be flaky — if a command returns empty, try again or use a simpler command

**RDP Connection (only when SSH won't work — GUI, launching runs):**
```bash
pkill -f xfreerdp 2>/dev/null; sleep 1
xfreerdp /v:aiagent.hostedremotedesktop.com:4420 /u:'VM-4420-11\AIAgent' /p:'01Hello02!@!' /dynamic-resolution /cert:ignore /auto-reconnect +clipboard &>/tmp/rdp2.log &
echo "RDP PID: $!"
sleep 10
echo "Done"
```

**RDP Reconnection Notes:**
- RDP sessions can drop between context windows — always reconnect at session start
- After reconnecting, the VM desktop state persists (windows stay open)
- QB windows may be behind other windows — use Alt+Tab or click taskbar icons to find them
- The Admin PowerShell window persists across RDP reconnects — look for it in taskbar
- If QB 2021 shows a password dialog behind other windows, use Alt+Tab to find it

**VM Credentials:**
- IP: `aiagent.hostedremotedesktop.com:4420` (RDP) / `aiagent.hostedremotedesktop.com` port 22 (SSH)
- User: `VM-4420-11\AIAgent`
- Pass: `01Hello02!@!`



**QuickBooks Installations:**
- QB 2023 (64-bit): `C:\Program Files\Intuit\QuickBooks 2023\QBWPremierAccountant.exe` — process name: `qbw.exe`
- QB 2021 (32-bit): `C:\Program Files (x86)\Intuit\QuickBooks 2021\QBW32PremierAccountant.exe` — process name: `QBW32.exe`
