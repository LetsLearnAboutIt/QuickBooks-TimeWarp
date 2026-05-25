# QuickBooks TimeWarp — UI Modernization Deploy Instructions

## Safety Backup Created

A backup branch has been created as a recovery point **before** the UI modernization commit is pushed to `origin/main`.

| Item | Value |
|------|-------|
| **Backup Branch** | `backup-pre-ui-modernization` |
| **Points To** | `c7cde4b` — Merge branch 'joseph-fixes' |
| **What It Preserves** | Exact state of `origin/main` before UI borderless chrome changes |
| **UI Commit** | `1bad464` — UI: Borderless window chrome with Intel Pro Graphics aesthetic |

### Files Changed in UI Modernization (11 files, +425 / −19 lines)

| File | Change |
|------|--------|
| `UI/Styles.xaml` | +178 lines — Window control buttons, thin scrollbar styles |
| `UI/Views/MainWindow.xaml` | +58 lines — Borderless window, custom title bar |
| `UI/Views/MainWindow.xaml.cs` | +60 lines — Drag, minimize, maximize, close handlers |
| `UI/Views/AboutWindow.xaml` | +28 lines — Borderless chrome, title bar |
| `UI/Views/AboutWindow.xaml.cs` | +7 lines — Title bar drag |
| `UI/Views/SettingsWindow.xaml` | +30 lines — Borderless chrome, title bar |
| `UI/Views/SettingsWindow.xaml.cs` | +6 lines — Title bar drag |
| `UI/Views/CompletionWindow.xaml` | +29 lines — Borderless chrome, title bar |
| `UI/Views/CompletionWindow.xaml.cs` | +6 lines — Title bar drag |
| `UI/Views/EulaWindow.xaml` | +36 lines — Borderless chrome, title bar |
| `UI/Views/EulaWindow.xaml.cs` | +6 lines — Title bar drag |

---

## Step 1: Push the Backup Branch (Safety Net)

```powershell
cd C:\path\to\QuickBooks-TimeWarp
git push origin backup-pre-ui-modernization
```

This pushes the backup to GitHub so you have a remote recovery point.

## Step 2: Push Main with UI Changes

```powershell
git push origin main
```

This pushes the single UI modernization commit (`1bad464`) to `origin/main`.

## Step 3: Verify on GitHub

After pushing, verify on GitHub that:
- `main` shows commit `1bad464` at the top
- `backup-pre-ui-modernization` branch exists and points to `c7cde4b`

---

## Emergency Rollback (If Needed)

### Option A: Reset main to backup (nuclear rollback)

```powershell
# Reset local main to the backup point
git checkout main
git reset --hard backup-pre-ui-modernization

# Force push to remote (⚠️ destructive — removes UI commit from remote)
git push origin main --force
```

### Option B: Revert the UI commit (preserves history)

```powershell
# Create a revert commit that undoes the UI changes
git checkout main
git revert 1bad464

# Push the revert (safe — no force push needed)
git push origin main
```

### Option C: Re-apply UI changes after rollback

```powershell
# If you rolled back but want the UI changes again later:
git cherry-pick 1bad464
git push origin main
```

---

## Cleanup (After Confirming Everything Works)

Once you've verified the UI changes work correctly in the built application:

```powershell
# Delete the backup branch locally
git branch -d backup-pre-ui-modernization

# Delete the backup branch on GitHub
git push origin --delete backup-pre-ui-modernization
```

---

*Generated: 2026-05-25 | QuickBooks TimeWarp v1.0.0*
