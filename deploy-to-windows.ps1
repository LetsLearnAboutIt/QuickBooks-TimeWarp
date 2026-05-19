# QB-TimeWarp Deployment Script - Run as Administrator in PowerShell
param(
    [string]$TargetPath = "C:\QB-TimeWarp"
)

$ErrorActionPreference = "Continue"

Write-Host "=== QB-TimeWarp Deployment ===" -ForegroundColor Cyan
Write-Host "Target: $TargetPath | $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Cyan

# Ensure directory structure
$dirs = @("Configuration", "Services", "Models", "Helpers", "Schemas", "ExportedData", "Logs", "Validation")
foreach ($d in $dirs) {
    $p = Join-Path $TargetPath $d
    if (-not (Test-Path $p)) { New-Item -Path $p -ItemType Directory -Force | Out-Null }
}
Write-Host "[OK] Directory structure verified" -ForegroundColor Green

# Check .NET SDK
$dotnetVer = & dotnet --version 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "[OK] .NET SDK: $dotnetVer" -ForegroundColor Green
} else {
    Write-Host "[FAIL] .NET SDK not found - install .NET 6.0 SDK" -ForegroundColor Red
    exit 1
}

# Check Git
$gitVer = & git --version 2>&1
if ($LASTEXITCODE -eq 0) { Write-Host "[OK] Git: $gitVer" -ForegroundColor Green }
else { Write-Host "[WARN] Git not installed" -ForegroundColor Yellow }

# Build application
Set-Location $TargetPath
Write-Host "`nRestoring packages..." -ForegroundColor Yellow
dotnet restore
if ($LASTEXITCODE -ne 0) { Write-Host "[FAIL] dotnet restore failed" -ForegroundColor Red; exit 1 }
Write-Host "[OK] Packages restored" -ForegroundColor Green

Write-Host "Building Release (win-x86)..." -ForegroundColor Yellow
dotnet build -c Release --runtime win-x86
if ($LASTEXITCODE -ne 0) { Write-Host "[FAIL] Build failed" -ForegroundColor Red; exit 1 }
Write-Host "[OK] Build succeeded" -ForegroundColor Green

# QuickBooks environment check
Write-Host "`n=== QuickBooks Environment ===" -ForegroundColor Cyan

$qbChecks = @{
    "QB 2023" = "C:\Program Files\Intuit\QuickBooks 2023"
    "QB 2021" = "C:\Program Files (x86)\Intuit\QuickBooks 2021"
}
foreach ($qb in $qbChecks.GetEnumerator()) {
    if (Test-Path $qb.Value) { Write-Host "[OK] $($qb.Key): $($qb.Value)" -ForegroundColor Green }
    else { Write-Host "[MISS] $($qb.Key): $($qb.Value)" -ForegroundColor Yellow }
}

# Search for company files
Write-Host "`nCompany files (.QBW):" -ForegroundColor Yellow
$qbwFiles = Get-ChildItem -Path C:\Users -Filter "*.QBW" -Recurse -ErrorAction SilentlyContinue
$qbwFiles += Get-ChildItem -Path "C:\Users\Public\Documents" -Filter "*.QBW" -Recurse -ErrorAction SilentlyContinue
foreach ($f in $qbwFiles) {
    Write-Host "  $($f.FullName) ($([math]::Round($f.Length/1MB,1)) MB)" -ForegroundColor Gray
}

# Check QBFC COM components
Write-Host "`nQBFC COM components:" -ForegroundColor Yellow
foreach ($ver in @("16","15","14")) {
    try {
        $obj = New-Object -ComObject "QBFC$ver.QBSessionManager" -ErrorAction Stop
        Write-Host "  [OK] QBFC$ver registered" -ForegroundColor Green
        [System.Runtime.InteropServices.Marshal]::ReleaseComObject($obj) | Out-Null
    } catch {
        Write-Host "  [--] QBFC$ver not available" -ForegroundColor Gray
    }
}

# Validate config
Write-Host "`n=== Configuration ===" -ForegroundColor Cyan
$configPath = Join-Path $TargetPath "appsettings.json"
if (Test-Path $configPath) {
    $cfg = Get-Content $configPath | ConvertFrom-Json
    Write-Host "[OK] QB2023 company: $($cfg.QuickBooks.QB2023.CompanyFilePath)" -ForegroundColor Green
    Write-Host "[OK] QB2021 company: $($cfg.QuickBooks.QB2021.CompanyFilePath)" -ForegroundColor Green
} else {
    Write-Host "[FAIL] appsettings.json not found" -ForegroundColor Red
}

$mappings = Join-Path $TargetPath "Configuration\FieldMappings.json"
if (Test-Path $mappings) {
    try { Get-Content $mappings -Raw | ConvertFrom-Json | Out-Null; Write-Host "[OK] FieldMappings.json valid" -ForegroundColor Green }
    catch { Write-Host "[FAIL] FieldMappings.json invalid JSON" -ForegroundColor Red }
}

Write-Host "`n=== Deployment Complete ===" -ForegroundColor Cyan
Write-Host "Run: dotnet run -- --mode schema-only   (test QB connectivity)" -ForegroundColor White
Write-Host "Run: dotnet run -- --mode export         (export from QB 2023)" -ForegroundColor White
Write-Host "Run: dotnet run -- --mode import         (import into QB 2021)" -ForegroundColor White
Write-Host "Run: dotnet run -- --mode validate       (verify migration)" -ForegroundColor White
