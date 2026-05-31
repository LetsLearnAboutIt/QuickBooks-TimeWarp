Testing QuickBooks interop locally
=================================

If you want to validate whether the QuickBooks SDK alone is sufficient for your certified connection on this machine, follow these steps:

1. Generate or copy `Interop.QBFC16.dll` into `lib/` (see `SDK_INSTRUCTIONS.md`).
2. Build the project:

```powershell
cd "c:\Users\jschr\OneDrive\Desktop\Visual Studio Projects\QuickBooks-TimeWarp"
dotnet build "QB-TimeWarp.csproj" -c Release
```

3. To actually exercise the SDK at runtime, you'll typically need QuickBooks Desktop present, unless your certificate allows headless connections via the SDK on the machine. If you have such a certificate and have set up the request processor, run your existing PowerShell scripts in this repo:

```powershell
./01-Certify_Client_Company_File.ps1
./02-ReCertify-Template.ps1
```

4. Review the logs produced by those scripts for a successful certificate-based connection. If they fail due to COM registration errors, QuickBooks/SDK isn't registered on this host and you'll need to test on your VM or install QuickBooks/SDK temporarily.
