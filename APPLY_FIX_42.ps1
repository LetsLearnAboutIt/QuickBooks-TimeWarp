Write-Host "FIX #42: Adding Memo to JournalEntries exclusion list" -ForegroundColor Cyan
Write-Host ""

$file = "C:\QB-TimeWarp\Services\DataImporter.cs"
$backup = "$file.backup_$(Get-Date -Format 'yyyyMMdd_HHmmss')"

# Backup
Copy-Item $file $backup
Write-Host "Backed up to: $backup" -ForegroundColor Green

# Read file
$content = Get-Content $file -Raw

# Simple replacement - find the line and add Memo if not already there
if ($content -match '"Memo"') {
    Write-Host "Memo already in exclusion list - skipping" -ForegroundColor Yellow
} else {
    # Replace the CreditTotal line to add Memo after it
    $content = $content -replace '("CreditTotal",\s*// computed rollup\r?\n\s*\},)', "`$1".Replace('},', "`"Memo`",              // not in JournalEntryAdd schema`r`n            },")

    # Simpler approach: just do a literal find/replace
    $old = @'
                "CreditTotal",       // computed rollup
            },
            ["Deposits"]
'@
    $new = @'
                "CreditTotal",       // computed rollup
                "Memo",              // FIX #42: Memo not in JournalEntryAdd schema
            },
            ["Deposits"]
'@
    
    # Re-read and do clean replacement
    $content = Get-Content $file -Raw
    $content = $content.Replace($old, $new)
    
    Set-Content $file $content -NoNewline
    
    # Verify
    $verify = Get-Content $file -Raw
    if ($verify -match '"Memo"') {
        Write-Host "Successfully added Memo to exclusion list" -ForegroundColor Green
    } else {
        Write-Host "ERROR: Failed to add Memo" -ForegroundColor Red
        Copy-Item $backup $file -Force
        exit 1
    }
}

Write-Host ""
Write-Host "Rebuilding..." -ForegroundColor Yellow
cd C:\QB-TimeWarp
dotnet clean -c Release | Out-Null
dotnet build -c Release

if ($LASTEXITCODE -ne 0) { exit 1 }

Write-Host ""
Write-Host "Preparing environment..." -ForegroundColor Yellow
Copy-Item appsettings.json bin\x86\Release\net6.0-windows\win-x86\ -Force
taskkill /F /IM QBW32.exe 2>$null | Out-Null
Stop-Service QuickBooksDB33 -Force -ErrorAction SilentlyContinue
Start-Sleep 3
Remove-Item 'C:\QB-TimeWarp\Working\Target\Blank_Template.qbw' -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item 'C:\QB-TimeWarp\Working\QB21_Blank_Template\Blank_Template.qbw' 'C:\QB-TimeWarp\Working\Target\Blank_Template.qbw' -Force
Start-Service QuickBooksDB33
Start-Sleep 2

Write-Host ""
Write-Host "Running migration..." -ForegroundColor Cyan
cd bin\x86\Release\net6.0-windows\win-x86
.\QB-TimeWarp.exe
