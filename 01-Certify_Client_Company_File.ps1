$f = "C:\Users\AIAgent\Desktop\Client_File_Staging\Secure_Staging.qbw"
Write-Host "QB-TimeWarp Certificate Approval" -ForegroundColor Cyan
if (-not (Test-Path $f)) { Write-Host "File not found" -ForegroundColor Red; exit 1 }
try {
$rp = New-Object -ComObject QBXMLRP2.RequestProcessor
Write-Host "Opening connection - WATCH FOR CERTIFICATE DIALOG" -ForegroundColor Yellow
$rp.OpenConnection2("", "QB-TimeWarp", 1)
$t = $rp.BeginSession("", 0)
Write-Host "Session OK! Ticket: $t" -ForegroundColor Green
$x = [char]60 + "?xml version=`"1.0`" encoding=`"utf-8`"?" + [char]62 + [char]60 + "?qbxml version=`"15.0`"?" + [char]62 + [char]60 + "QBXML" + [char]62 + [char]60 + "QBXMLMsgsRq onError=`"continueOnError`"" + [char]62 + [char]60 + "CompanyQueryRq requestID=`"1`"" + [char]62 + [char]60 + "/CompanyQueryRq" + [char]62 + [char]60 + "/QBXMLMsgsRq" + [char]62 + [char]60 + "/QBXML" + [char]62
$r = $rp.ProcessRequest($t, $x)
if ($r -match "<CompanyName>([^<]+)</CompanyName>") { Write-Host "Company: $($Matches[1])" -ForegroundColor Green }
$rp.EndSession($t)
$rp.CloseConnection()
Write-Host "CERTIFICATE APPROVAL COMPLETE" -ForegroundColor Green
} catch { Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red }
