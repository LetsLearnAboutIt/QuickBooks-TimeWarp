# QB-TimeWarp FIX #41 - Corrected Amount Field Fix
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "QB-TimeWarp FIX #41 - Auto Apply & Run" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$file = "C:\QB-TimeWarp\Services\DataTransformer.cs"
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$backup = "C:\QB-TimeWarp\Services\DataTransformer.cs.backup_$timestamp"

# Step 1: Backup
Write-Host "[1/7] Creating backup..." -ForegroundColor Yellow
Copy-Item $file $backup -Force
Write-Host "  OK Backup created: $backup" -ForegroundColor Green
Write-Host ""

# Step 2: Apply fixes
Write-Host "[2/7] Applying FIX #41 corrections..." -ForegroundColor Yellow
$content = Get-Content $file -Raw
$original = $content

$content = $content -replace '\["DebitAmount"\]', '["Amount"]'
$content = $content -replace '\["CreditAmount"\]', '["Amount"]'

Set-Content $file $content -NoNewline

# Step 3: Verify
Write-Host "[3/7] Verifying changes..." -ForegroundColor Yellow
$debitCount = (Select-String -Path $file -Pattern 'DebitAmount' -AllMatches).Matches.Count
$creditCount = (Select-String -Path $file -Pattern 'CreditAmount' -AllMatches).Matches.Count
$amountCount = (Select-String -Path $file -Pattern '\["Amount"\]' -AllMatches).Matches.Count

if ($debitCount -gt 0 -or $creditCount -gt 0) {
    Write-Host "  ERROR: Still found DebitAmount or CreditAmount!" -ForegroundColor Red
    Set-Content $file $original -NoNewline
    Write-Host "  Rolled back changes." -ForegroundColor Red
    Write-Host ""
    exit 1
}
Write-Host "  OK All field names corrected to Amount - found $amountCount instances" -ForegroundColor Green
Write-Host ""

# Step 4: Clean build
Write-Host "[4/7] Cleaning previous build..." -ForegroundColor Yellow
cd C:\QB-TimeWarp
dotnet clean -c Release | Out-Null
Write-Host "  OK Clean complete" -ForegroundColor Green
Write-Host ""

# Step 5: Rebuild
Write-Host "[5/7] Building with FIX #41..." -ForegroundColor Yellow
dotnet build -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "  ERROR: Build failed!" -ForegroundColor Red
    Write-Host ""
    exit 1
}
Write-Host "  OK Build successful" -ForegroundColor Green
Write-Host ""

# Step 6: Prepare QB environment
Write-Host "[6/7] Preparing QuickBooks environment..." -ForegroundColor Yellow
Copy-Item appsettings.json bin\x86\Release\net6.0-windows\win-x86\ -Force
taskkill /F /IM QBW32.exe 2>$null | Out-Null
taskkill /F /IM qbw.exe 2>$null | Out-Null
Stop-Service QuickBooksDB33 -Force -ErrorAction SilentlyContinue
Start-Sleep 3
Remove-Item 'C:\QB-TimeWarp\Working\Target\Blank_Template.qbw' -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item 'C:\QB-TimeWarp\Working\QB21_Blank_Template\Blank_Template.qbw' 'C:\QB-TimeWarp\Working\Target\Blank_Template.qbw' -Force
Start-Service QuickBooksDB33
Start-Sleep 2
Write-Host "  OK Environment ready" -ForegroundColor Green
Write-Host ""

# Step 7: Run migration
Write-Host "[7/7] Running migration with corrected FIX #41..." -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
cd bin\x86\Release\net6.0-windows\win-x86
.\QB-TimeWarp.exe
$exitCode = $LASTEXITCODE

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
if($exitCode -eq 0) {
    Write-Host "Migration completed successfully!" -ForegroundColor Green
} else {
    Write-Host "Migration completed with exit code: $exitCode" -ForegroundColor Red
}
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
