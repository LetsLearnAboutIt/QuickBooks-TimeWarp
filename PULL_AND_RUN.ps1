Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "FIX #44 + #45 - Git Pull & Migration" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

cd C:\QB-TimeWarp

# Step 1: Git Pull
Write-Host "[1/4] Pulling latest changes from GitHub..." -ForegroundColor Yellow
git pull origin main
if ($LASTEXITCODE -ne 0) {
    Write-Host "  ERROR: Git pull failed!" -ForegroundColor Red
    Write-Host "  Try: git reset --hard origin/main" -ForegroundColor Yellow
    exit 1
}
Write-Host "  OK Latest code pulled" -ForegroundColor Green
Write-Host ""

# Step 2: Clean and rebuild
Write-Host "[2/4] Rebuilding solution..." -ForegroundColor Yellow
dotnet clean -c Release | Out-Null
dotnet build -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "  ERROR: Build failed!" -ForegroundColor Red
    exit 1
}
Write-Host "  OK Build successful" -ForegroundColor Green
Write-Host ""

# Step 3: Prepare environment
Write-Host "[3/4] Preparing QuickBooks environment..." -ForegroundColor Yellow
Copy-Item appsettings.json bin\x86\Release\net6.0-windows\win-x86\ -Force
taskkill /F /IM QBW32.exe 2>$null | Out-Null
Stop-Service QuickBooksDB33 -Force -ErrorAction SilentlyContinue
Start-Sleep 3
Remove-Item 'C:\QB-TimeWarp\Working\Target\Blank_Template.qbw' -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item 'C:\QB-TimeWarp\Working\QB21_Blank_Template\Blank_Template.qbw' 'C:\QB-TimeWarp\Working\Target\Blank_Template.qbw' -Force
Start-Service QuickBooksDB33
Start-Sleep 2
Write-Host "  OK Environment ready" -ForegroundColor Green
Write-Host ""

# Step 4: Run migration
Write-Host "[4/4] Running migration with FIX #44 + #45..." -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
cd bin\x86\Release\net6.0-windows\win-x86
.\QB-TimeWarp.exe

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Migration Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Expected results:" -ForegroundColor Yellow
Write-Host "  - JournalEntries should use signed amounts (positive=debit, negative=credit)" -ForegroundColor White
Write-Host "  - Bank account 1-1020 should be properly balanced" -ForegroundColor White
Write-Host "  - Payroll expenses should appear in 6-6600" -ForegroundColor White
Write-Host ""
