# ============================================================
# QB-TimeWarp: QUICK REBUILD & TEST SCRIPT
# Run this in PowerShell on the Windows machine
# ============================================================

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " QB-TimeWarp: Quick Rebuild & Test" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$projectRoot = "C:\QB-TimeWarp"
$binDir = "$projectRoot\bin\x86\Release\net6.0-windows\win-x86"
$targetDir = "$projectRoot\Working\Target"
$targetFile = "$targetDir\Blank_Template.qbw"
$masterTemplate = "$projectRoot\Working\QB21_Blank_Template.qbw"

# --- Step 0: Pull latest from GitHub ---
Write-Host "[Step 0] Pulling latest code from GitHub..." -ForegroundColor Yellow
Set-Location $projectRoot
git pull origin main
Write-Host ""

# --- Step 1: Clean Build ---
Write-Host "[Step 1] Clean Build..." -ForegroundColor Yellow
dotnet clean 2>&1 | Out-Null
$buildOutput = dotnet build -c Release -r win-x86 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "BUILD FAILED!" -ForegroundColor Red
    $buildOutput | Write-Host
    exit 1
}
Write-Host "Build succeeded!" -ForegroundColor Green
Write-Host ""

# --- Step 2: Copy appsettings.json ---
Write-Host "[Step 2] Copying appsettings.json to output..." -ForegroundColor Yellow
Copy-Item "$projectRoot\appsettings.json" "$binDir\" -Force
Write-Host "Done." -ForegroundColor Green
Write-Host ""

# --- Step 3: Reset Target (SAFETY PROTOCOL) ---
Write-Host "[Step 3] Resetting Target file from Master Template..." -ForegroundColor Yellow
if (Test-Path $targetFile) {
    Remove-Item $targetFile -Force
    Write-Host "  Deleted old target." -ForegroundColor DarkYellow
}
Copy-Item $masterTemplate $targetFile -Force
Write-Host "  Fresh copy in place." -ForegroundColor Green
Write-Host ""

# --- Step 4: Run Migration ---
Write-Host "[Step 4] Running QB-TimeWarp.exe..." -ForegroundColor Yellow
Write-Host "  (Make sure QuickBooks 2021 is CLOSED before proceeding)" -ForegroundColor Magenta
Write-Host ""
Set-Location $binDir
& ".\QB-TimeWarp.exe" 2>&1 | Tee-Object -Variable migrationOutput

# --- Step 5: Show Summary ---
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " MIGRATION COMPLETE - Key Results:" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
$migrationOutput | Select-String -Pattern "SalesTaxPaymentCheck|SUMMARY|Total|Success|Failed|Error" | ForEach-Object { Write-Host $_.Line -ForegroundColor White }
