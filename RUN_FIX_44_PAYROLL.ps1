Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "FIX #44 - Payroll Export & Conversion" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"

Write-Host "[1/4] Backing up files..." -ForegroundColor Yellow
Copy-Item "C:\QB-TimeWarp\Services\DataExporter.cs" "C:\QB-TimeWarp\Services\DataExporter.cs.backup_$timestamp" -Force
Copy-Item "C:\QB-TimeWarp\Services\DataTransformer.cs" "C:\QB-TimeWarp\Services\DataTransformer.cs.backup_$timestamp" -Force
Copy-Item "C:\QB-TimeWarp\appsettings.json" "C:\QB-TimeWarp\appsettings.json.backup_$timestamp" -Force
Write-Host "  OK Backups created" -ForegroundColor Green
Write-Host ""

Write-Host "[2/4] Rebuilding with FIX #44..." -ForegroundColor Yellow
cd C:\QB-TimeWarp
dotnet clean -c Release | Out-Null
dotnet build -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "  ERROR: Build failed!" -ForegroundColor Red
    exit 1
}
Write-Host "  OK Build successful" -ForegroundColor Green
Write-Host ""

Write-Host "[3/4] Preparing environment..." -ForegroundColor Yellow
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

Write-Host "[4/4] Running migration with FIX #44..." -ForegroundColor Cyan
Write-Host "  Look for [FIX #44] in the log output" -ForegroundColor Yellow
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
Write-Host "  - Payroll expenses should now show in 6-6600" -ForegroundColor White
Write-Host "  - Bank account 1-1020 should be credited properly" -ForegroundColor White
Write-Host "  - Check for [FIX #44] logs showing paycheck conversions" -ForegroundColor White
Write-Host ""
