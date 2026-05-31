SDK interop instructions
=========================

Purpose
-------
This file explains how to produce or copy the QuickBooks COM interop assemblies (`Interop.QBFC16.dll` / `Interop.QBFC15.dll`) from a machine that has the QuickBooks SDK installed and place them into this project's `lib/` folder so the project can compile on machines without QuickBooks installed.

License compliance
------------------
The QuickBooks SDK license explicitly forbids redistributing QBFC, RDS, the QBO connector, or the web connector as raw DLLs. This repository only uses `lib/` for local development and build support on SDK-enabled machines.

- Do not check `Interop.QBFC*.dll` into source control.
- Do not distribute these files as part of your application installer.
- For shipping, use Intuit’s official stand-alone installers or merge modules as required by the SDK license.

Merge module placement
----------------------
If you have an Intuit merge module (`.msm`), place it in the `Installer/` folder, not in `lib/`.
`lib/` remains reserved for local-only runtime/build artifacts such as generated `Interop.QBFC*.dll`.

Option A — generate interop using `tlbimp.exe` (recommended)
-----------------------------------------------------------
1. On your SDK/VM machine locate the QBFC type library, for example:

   `C:\Program Files (x86)\Intuit\IDN\QBSDK16.0\tools\QBFC16\QBFC16Lib.dll`

2. Run `tlbimp` (Developer Command Prompt / elevated PowerShell):

```powershell
tlbimp "C:\Program Files (x86)\Intuit\IDN\QBSDK16.0\tools\QBFC16\QBFC16Lib.dll" /out:Interop.QBFC16.dll
``` 

3. Copy the generated `Interop.QBFC16.dll` file to this repo's `lib\` folder.

Option B — copy an existing interop assembly
--------------------------------------------
If you've previously added the COM reference in Visual Studio on the SDK machine, Visual Studio may have produced an interop assembly (commonly located in the project's `obj\` folder). Copy that `Interop.QBFC16.dll` into `lib\`.

After copying/generating
-----------------------
1. Rebuild the project locally:

```powershell
cd "c:\Users\jschr\OneDrive\Desktop\Visual Studio Projects\QuickBooks-TimeWarp"
dotnet build "QB-TimeWarp.csproj" -c Release
```

2. If the build fails with missing types at runtime, note that the QuickBooks COM server (QuickBooks Desktop) must be present to actually run QB calls — the interop only satisfies compile-time.

License/Redistribution
----------------------
Confirm your licensing and redistribution rights before committing SDK or interop binaries into source control. If unsure, keep the interop DLL out of version control and share it to test machines via a secure artifact or installer step.
