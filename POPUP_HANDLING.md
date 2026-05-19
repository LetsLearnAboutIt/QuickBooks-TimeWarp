# QB-TimeWarp — QuickBooks Popup Handling Guide

> **Purpose**: Reference guide for handling common QuickBooks popups that appear during SDK operations and testing. Keep QuickBooks windows VISIBLE at all times to catch these.

---

## ⚠️ Golden Rule

**If you see a popup you don't recognize → STOP and check with the user before clicking anything.**

QuickBooks popups can block SDK operations silently. The application may appear "hung" when it's actually waiting for a popup to be dismissed.

---

## 1. SDK Permission / Access Request

### What it looks like
> **"An application is requesting access to QuickBooks company data"**
> or
> **"Do you want to allow this application to read and modify this company file?"**

### What to do
- Select **"Yes, always; allow access even if QuickBooks is not running"**
- This is the QBXML SDK requesting COM access to the company file
- If you select "No" or "Deny", the SDK connection will fail with a COM error

### When it appears
- First time running QB-TimeWarp against a company file
- After QuickBooks updates
- After changing the application's executable path

---

## 2. Company File Password Prompt

### What it looks like
> **"Enter password for [Company Name]"**
> or
> **"QuickBooks Login"** dialog with username/password fields

### What to do
Enter the correct password:

| Company File | Password |
|-------------|----------|
| Air-Masters-QB-2023.qbw | `3825You171` |
| Josh Safty 2021.qbw | `3825You171` |
| Blank_Template.qbw (QB21_Blank_Template) | `Fl0640098!@!` |

### When it appears
- When opening a company file
- When the SDK attempts to connect to a password-protected company file
- After a session timeout

---

## 3. Multi-User Mode Warnings

### What it looks like
> **"This company file is in multi-user mode"**
> or
> **"Another user is currently logged into this company file"**
> or
> **"QuickBooks needs to switch to single-user mode"**

### What to do
1. Switch to **single-user mode**: In QuickBooks, go to **File → Switch to Single-user Mode**
2. If another user is logged in, they must log out first
3. The QBXML SDK works most reliably in single-user mode

### When it appears
- When QuickBooks Database Server Manager is running
- When the file was previously opened in multi-user mode
- When another application has a connection to the file

---

## 4. QuickBooks Update Prompts

### What it looks like
> **"A new update is available for QuickBooks"**
> or
> **"QuickBooks needs to download updates"**

### What to do
- Click **"Skip"**, **"Remind me later"**, or **"No"**
- **Do NOT update QuickBooks during a migration** — updates can change SDK behavior, field schemas, or break COM interop
- Schedule updates for after the migration is complete

### When it appears
- On QuickBooks startup
- Periodically during operation if auto-update is enabled

---

## 5. Registration / License Dialogs

### What it looks like
> **"Register QuickBooks"**
> or
> **"Your QuickBooks license..."**
> or
> **"Product registration required"**

### What to do
- Click **"Remind Me Later"** or **close/dismiss** the dialog
- Do NOT start a registration or licensing process during testing
- These dialogs can block the main window and prevent SDK operations

### When it appears
- On QuickBooks startup (trial or unregistered copies)
- After system date changes
- Periodically as license check reminders

---

## 6. Backup Prompts

### What it looks like
> **"Back up your company file?"**
> or
> **"QuickBooks recommends backing up before closing"**
> or
> **"Do you want to back up now?"**

### What to do
- During testing: Click **"No"** or **"Skip"** — backups take time and can interfere
- Before actual migration: **Yes, back up!** — always have a backup before writing to QB 2021
- The backup prompt typically appears when closing QuickBooks or the company file

### When it appears
- When closing QuickBooks
- When switching company files
- Based on scheduled backup reminders

---

## 7. "Company File in Use" / File Lock Errors

### What it looks like
> **"This file is in use by another application"**
> or
> **"QuickBooks could not open the company file"**

### What to do
1. Check if another QuickBooks instance has the file open
2. Check Task Manager for any running `QBW32.exe` or `qbupdate.exe` processes
3. Close other instances and retry
4. If the file is locked by the SDK, restart QuickBooks

### When it appears
- When trying to open a company file that's already open
- After a previous SDK session didn't close cleanly
- When QuickBooks crashed and left a lock file

---

## 8. "Rebuild Data" or "Verify Data" Suggestions

### What it looks like
> **"QuickBooks has detected a problem with your data"**
> or
> **"Would you like to rebuild your data?"**
> or
> **"Run Verify Data to check for data issues"**

### What to do
- **STOP and check with user** — this could indicate data corruption
- If prompted during testing on the **target** (QB 2021) file, it may indicate import issues
- On the **source** (QB 2023) file, it's a pre-existing condition — note it but don't try to fix it
- Rebuilding data takes a long time on large files (~360 MB) — coordinate with user

### When it appears
- After QuickBooks detects inconsistencies
- During company file open on damaged files
- After power loss or crash during write operations

---

## 9. Intuit/QuickBooks Online Advertising Popups

### What it looks like
> **"Try QuickBooks Online"**
> or
> **"Upgrade to QuickBooks [newer version]"**
> or various promotional/advertising dialogs

### What to do
- Click **"No thanks"**, **"Close"**, or the **X** button
- These are advertising and have no operational impact
- They can block the main window though, so dismiss them promptly

### When it appears
- On startup
- Periodically during use

---

## 10. Windows UAC (User Account Control)

### What it looks like
> **"Do you want to allow this app to make changes to your device?"**
> (Windows system dialog with dimmed background)

### What to do
- Click **"Yes"** — QuickBooks and the SDK need elevated permissions for COM registration
- If you click "No", the SDK may fail to connect

### When it appears
- When running QB-TimeWarp for the first time
- When QuickBooks needs to update COM registrations

---

## Unknown Popups — What to Do

If you encounter a popup not listed above:

1. **STOP** — do not click anything
2. **Screenshot** the popup (or describe it to the user)
3. **Check with the user** — they may recognize it
4. **Read the text carefully** — look for keywords:
   - "access", "permission", "allow" → Likely SDK permission (usually safe to approve)
   - "update", "upgrade" → Skip/dismiss
   - "error", "problem", "corrupt" → STOP and investigate
   - "backup" → Usually safe to skip during testing
   - "password", "login" → Enter credentials from PASSWORDS.txt
5. **Document it** — add it to this guide for future reference

---

## Popup Prevention Tips

1. **Disable auto-updates**: In QuickBooks → Help → Update QuickBooks → Options → turn off automatic updates
2. **Pre-authorize the SDK**: Run QB-TimeWarp once with the company file open and approve the access dialog — subsequent runs won't prompt
3. **Use single-user mode**: Prevents multi-user warnings
4. **Open company files before starting**: This clears password prompts upfront
5. **Dismiss all popups before running**: Open QuickBooks, dismiss any startup dialogs, THEN run the migration tool
