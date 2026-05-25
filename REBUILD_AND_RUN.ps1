Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Rebuilding with FIX #42 Applied" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

cd C:\QB-TimeWarp

Write-Host "[1/3] Cleaning..." -ForegroundColor Yellow
dotnet clean -c Release | Out-Null
Write-Host "  OK" -ForegroundColor Green

Write-Host "[2/3] Building..." -ForegroundColor Yellow
dotnet build -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "  ERROR: Build failed!" -ForegroundColor Red
    exit 1
}
Write-Host "  OK" -ForegroundColor Green
Write-Host ""

Write-Host "[3/3] Preparing & Running Migration..." -ForegroundColor Yellow
Copy-Item appsettings.json bin\x86\Release\net6.0-windows\win-x86\ -Force
taskkill /F /IM QBW32.exe 2>$null | Out-Null
taskkill /F /IM qbw.exe 2>$null | Out-Null
Stop-Service QuickBooksDB33 -Force -ErrorAction SilentlyContinue
Start-Sleep 3
Remove-Item 'C:\QB-TimeWarp\Working\Target\Blank_Template.qbw' -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item 'C:\QB-TimeWarp\Working\QB21_Blank_Template\Blank_Template.qbw' 'C:\QB-TimeWarp\Working\Target\Blank_Template.qbw' -Force
Start-Service QuickBooksDB33
Start-Sleep 2

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Starting Migration with FIX #41 + #42" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

cd bin\x86\Release\net6.0-windows\win-x86
.\QB-TimeWarp.exe

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Migration Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
