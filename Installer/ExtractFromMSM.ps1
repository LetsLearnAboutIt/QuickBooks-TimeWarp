<#
ExtractFromMSM.ps1
Extract QBFC merge-module contents into Installer\Extracted using the project helper script.
#>
param(
    [string]$MsmFile = "Installer\QBFC16_0.msm",
    [string]$OutDir = "Installer\Extracted"
)

Write-Host "Extracting $MsmFile..."
python extract_msm.py
if (-not (Test-Path "tmp_msm_extract/QBFC16_0.cab")) {
    Write-Error "CAB payload not found. Run extract_msm.py on the MSM first."
    exit 1
}

New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
expand.exe tmp_msm_extract/QBFC16_0.cab -F:* tmp_msm_extract/extracted
Move-Item tmp_msm_extract/extracted/* $OutDir -Force
Write-Host "Files extracted to $OutDir"
