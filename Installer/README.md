Installer packaging guidance
===========================

Use this folder for installer-related assets such as Intuit merge modules.

Recommended layout:

- `Installer/`
  - `MergeModules/`
    - `QBFC16.msm`
    - `QBFC15.msm`
  - `Scripts/`
    - `InstallQBSDKFromMSM.ps1`
    - `UninstallQBSDKFromMSM.ps1`
  - `README.md`

Why not `lib/`?
--------------
- `lib/` is for local development assemblies and runtime binaries, not installer assets.
- A merge module is an installer/package fragment, not a runtime DLL.
- Keeping `.msm` in `Installer/` makes it clear that it is part of packaging, not application code.

How to use a merge module
-------------------------
- Merge modules are consumed by your MSI or installer authoring process.
- They are not directly referenced by the app at runtime.
- You should not treat them like a library dependency in `QB-TimeWarp.csproj`.

Local build guidance
--------------------
- For local builds on a machine with the QuickBooks SDK, continue to use `lib/Interop.QBFC16.dll` as a local-only compile-time dependency.
- Do not drop `.msm` files into `lib/`.

Compliance note
---------------
- The `.msm` file is the correct packaging artifact for Intuit-approved distribution.
- If you need to install the SDK temporarily for testing, use the official installer or merge-module-based packaging flow.
