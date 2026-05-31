<#
UninstallQBSDKFromMSM.ps1
Template to uninstall QuickBooks SDK/MSI payloads.
Usage examples:
  .\UninstallQBSDKFromMSM.ps1 -ProductCode "{XXXXX-...}"
  .\UninstallQBSDKFromMSM.ps1 -DisplayNamePattern "QBFC"    # attempts to find a matching installed product
  .\UninstallQBSDKFromMSM.ps1 -DeployedPath "C:\Program Files (x86)\QBFC_Manual\20260530..." -RemoveFiles

Notes:
- If you have the MSI ProductCode, pass it with -ProductCode for a clean msiexec uninstall.
- If you used the install script's -DeployExtracted to copy files, point -DeployedPath to that folder to remove files.
- Administrative rights are required to uninstall.
#>
param(
    [string]$ProductCode,
    [string]$DisplayNamePattern = "QBFC",
    [string]$DeployedPath,
    [switch]$RemoveFiles,
    [switch]$Force
)

function Assert-RunningAsAdmin {
    $current = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($current)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Write-Error "This script must be run as Administrator. Rerun in an elevated PowerShell session."
        exit 1
    }
}

Assert-RunningAsAdmin

if ($ProductCode) {
    Write-Host "Uninstalling via msiexec product code: $ProductCode"
    $log = "$(Join-Path $env:TEMP "qbfc_uninstall_$(Get-Date -Format yyyyMMddHHmmss).log")"
    $args = "/x $ProductCode /qn /l*v `"$log`""
    Start-Process -FilePath msiexec.exe -ArgumentList $args -Wait -NoNewWindow
    if ($LASTEXITCODE -ne 0) {
        Write-Error "msiexec returned exit code $LASTEXITCODE. See log: $log"
        exit $LASTEXITCODE
    }
    Write-Host "Uninstall complete. Log: $log"
    exit 0
}

# Try to find installed product by display name
$found = @()
$hives = @("HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall", "HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall")
foreach ($h in $hives) {
    if (Test-Path $h) {
        Get-ChildItem $h | ForEach-Object {
            $displayName = (Get-ItemProperty $_.PsPath -ErrorAction SilentlyContinue).DisplayName
            $productCode = $_.PSChildName
            if ($displayName -and $displayName -like "*$DisplayNamePattern*") {
                $found += [PSCustomObject]@{ Name = $displayName; ProductCode = $productCode }
            }
        }
    }
}

if ($found.Count -eq 0) {
    Write-Warning "No installed product matching '$DisplayNamePattern' found in registry."
} elseif ($found.Count -gt 1) {
    Write-Host "Multiple products matched:"; $found | Format-Table -AutoSize
    Write-Host "Run again with -ProductCode <ProductCode> to select the product to uninstall."
    exit 2
} else {
    $pc = $found[0].ProductCode
    Write-Host "Uninstalling $($found[0].Name) (ProductCode: $pc)"
    $log = "$(Join-Path $env:TEMP "qbfc_uninstall_$(Get-Date -Format yyyyMMddHHmmss).log")"
    $args = "/x $pc /qn /l*v `"$log`""
    Start-Process -FilePath msiexec.exe -ArgumentList $args -Wait -NoNewWindow
    if ($LASTEXITCODE -ne 0) {
        Write-Error "msiexec returned exit code $LASTEXITCODE. See log: $log"
        exit $LASTEXITCODE
    }
    Write-Host "Uninstall complete. Log: $log"
    exit 0
}

# If here, no MSI uninstall performed. Remove deployed files if requested.
if ($DeployedPath -and (Test-Path $DeployedPath)) {
    if ($RemoveFiles) {
        Write-Host "Removing deployed files at $DeployedPath"
        Remove-Item -Path $DeployedPath -Recurse -Force
        Write-Host "Files removed."
        exit 0
    } else {
        Write-Host "Deployed files exist at $DeployedPath. Use -RemoveFiles to delete them."
        exit 3
    }
}

Write-Host "No uninstall action performed. Provide -ProductCode, -DisplayNamePattern, or -DeployedPath with -RemoveFiles."
exit 4
