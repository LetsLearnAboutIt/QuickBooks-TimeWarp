Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "FIX #43 - Bank Transaction Diagnostics" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"

# Step 1: Backup current file
Write-Host "[1/5] Backing up current DataImporter.cs..." -ForegroundColor Yellow
Copy-Item "C:\QB-TimeWarp\Services\DataImporter.cs" "C:\QB-TimeWarp\Services\DataImporter.cs.backup_$timestamp" -Force
Write-Host "  OK Backup created" -ForegroundColor Green
Write-Host ""

# Step 2: Note - Manual file transfer needed
Write-Host "[2/5] File Transfer Status..." -ForegroundColor Yellow
Write-Host "  NOTE: Updated DataImporter.cs needs to be transferred from Linux VM" -ForegroundColor Yellow
Write-Host "  You'll need to manually copy the file or we can script the transfer" -ForegroundColor Yellow
Write-Host "  Press any key to continue if file is ready, or Ctrl+C to abort..." -ForegroundColor Yellow
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
Write-Host ""

# Step 3: Clean and rebuild
Write-Host "[3/5] Cleaning and rebuilding..." -ForegroundColor Yellow
cd C:\QB-TimeWarp
dotnet clean -c Release | Out-Null
dotnet build -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "  ERROR: Build failed!" -ForegroundColor Red
    exit 1
}
Write-Host "  OK Build successful" -ForegroundColor Green
Write-Host ""

# Step 4: Prepare environment
Write-Host "[4/5] Preparing QuickBooks environment..." -ForegroundColor Yellow
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

# Step 5: Run with diagnostics
Write-Host "[5/5] Running migration with FIX #43 diagnostics..." -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
cd bin\x86\Release\net6.0-windows\win-x86
.\QB-TimeWarp.exe

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Migration Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Diagnostic files should be in:" -ForegroundColor Yellow
Write-Host "  C:\QB-TimeWarp\Diagnostics\FIX43_BankTxnDiagnostic_*.json" -ForegroundColor White
Write-Host ""
Write-Host "Check the log for FIX #43 warnings:" -ForegroundColor Yellow
Write-Host "  C:\QB-TimeWarp\Logs\QB-TimeWarp-$timestamp.log" -ForegroundColor White
Write-Host ""
