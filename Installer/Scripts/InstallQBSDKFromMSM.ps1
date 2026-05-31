<#
InstallQBSDKFromMSM.ps1
Template to install QuickBooks SDK payloads (MSI or extracted MSM contents).
Usage examples:
  .\InstallQBSDKFromMSM.ps1 -InstallerPath "Installer\QBFC16_0.msm"
  .\InstallQBSDKFromMSM.ps1 -InstallerPath "path\to\qbfc.msi" -Force

Notes:
- Merge modules (.msm) cannot be installed directly. This script will extract the MSM (via existing helper) and look for an MSI inside the payload. If an MSI is found, it will install it silently via msiexec.
- If no MSI is present, the script can optionally copy extracted files to a staging folder for manual packaging/registration using -DeployExtracted.
- Administrative rights are required for installation.
#>
param(
    [string]$InstallerPath = "Installer\QBFC16_0.msm",
    [switch]$DeployExtracted,
    [string]$DeployTarget = "$env:ProgramFiles(x86)\\QBFC_Manual",
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

function Extract-MsmIfNeeded {
    param($MsmPath)
    $tmpCab = "tmp_msm_extract/QBFC16_0.cab"
    $tmpExtractDir = "tmp_msm_extract/extracted"
    if (-not (Test-Path $MsmPath)) {
        Write-Error "Installer path not found: $MsmPath"
        exit 1
    }
    if ($MsmPath.ToLower().EndsWith('.msm')) {
        Write-Host "Extracting CAB payload from $MsmPath..."
        # use existing python helper if present
        if (Test-Path "extract_msm.py") {
            python extract_msm.py | Out-Null
        } else {
            Write-Error "extract_msm.py helper not found. Please run ExtractFromMSM.ps1 or provide an MSI directly."
            exit 1
        }
        if (-not (Test-Path $tmpCab)) {
            Write-Error "CAB payload not found after extraction. Aborting."
            exit 1
        }
        Remove-Item -Recurse -Force $tmpExtractDir -ErrorAction SilentlyContinue
        New-Item -ItemType Directory -Path $tmpExtractDir | Out-Null
        Write-Host "Expanding CAB..."
        expand.exe $tmpCab -F:* $tmpExtractDir | Out-Null
        return $tmpExtractDir
    }
    return $null
}

Assert-RunningAsAdmin

$installerFull = Resolve-Path -Path $InstallerPath -ErrorAction SilentlyContinue
if (-not $installerFull) {
    Write-Error "Installer not found: $InstallerPath"
    exit 1
}
$installerFull = $installerFull.Path

if ($installerFull.ToLower().EndsWith('.msm')) {
    $extractedDir = Extract-MsmIfNeeded -MsmPath $installerFull
    $msiFiles = Get-ChildItem -Path $extractedDir -Filter *.msi -Recurse -ErrorAction SilentlyContinue
    if ($msiFiles -and $msiFiles.Count -gt 0) {
        $msi = $msiFiles[0].FullName
        Write-Host "Found MSI inside MSM payload: $msi"
        $log = "$(Join-Path $env:TEMP "qbfc_install_$(Get-Date -Format yyyyMMddHHmmss).log")"
        $args = "/i `"$msi`" /qn /norestart /l*v `"$log`""
        Write-Host "Running: msiexec $args"
        $proc = Start-Process -FilePath msiexec.exe -ArgumentList $args -Wait -PassThru
        if ($proc.ExitCode -ne 0) {
            Write-Error "msiexec returned exit code $($proc.ExitCode). See log: $log"
            exit $proc.ExitCode
        }
        Write-Host "Installation succeeded. Log: $log"
        exit 0
    }

    Write-Warning "No MSI found inside MSM payload. Merge modules (.msm) are intended to be included into an MSI during packaging."
    if ($DeployExtracted) {
        $target = Join-Path -Path $DeployTarget -ChildPath (Get-Date -Format yyyyMMddHHmmss)
        Write-Host "Deploying extracted files to: $target"
        New-Item -ItemType Directory -Path $target -Force | Out-Null
        Copy-Item -Path (Join-Path $extractedDir '*') -Destination $target -Recurse -Force
        Write-Host "Files copied to $target. You must register or package these files using your installer authoring tool."
        exit 0
    }

    Write-Host "No action taken. To proceed, either provide an MSI, include the .msm into your MSI at packaging time, or run this script with -DeployExtracted to copy files for manual packaging."
    exit 2
}

# If path is an MSI, install it directly
if ($installerFull.ToLower().EndsWith('.msi')) {
    $log = "$(Join-Path $env:TEMP "qbfc_install_$(Get-Date -Format yyyyMMddHHmmss).log")"
    $args = "/i `"$installerFull`" /qn /norestart /l*v `"$log`""
    Write-Host "Installing MSI: $installerFull"
    Start-Process -FilePath msiexec.exe -ArgumentList $args -Wait -NoNewWindow
    if ($LASTEXITCODE -ne 0) {
        Write-Error "msiexec returned exit code $LASTEXITCODE. See log: $log"
        exit $LASTEXITCODE
    }
    Write-Host "Installation complete. Log: $log"
    exit 0
}

Write-Error "Unsupported installer type. Provide a .msi or .msm file."
exit 1
