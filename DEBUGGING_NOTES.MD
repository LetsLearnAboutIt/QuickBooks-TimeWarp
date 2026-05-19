# QB-TimeWarp — Debugging Notes: 100% Import Failure Analysis

> **Date**: May 19, 2026  
> **Status**: 🔴 CRITICAL — 0/2072 records imported (100% failure rate)  
> **Error**: `0x80040400` — "QuickBooks found an error when parsing the provided XML text stream"

---

## 1. EXECUTIVE SUMMARY

Every single import attempt fails. There are **two distinct failure modes**:

| Failure Mode | Records | Error | Root Cause |
|---|---|---|---|
| **Mode A: "null" error** | First ~6 accounts (simple, no ParentRef) | `Skipped 'Name': null` | QBXML response returns no status — likely the XML envelope itself is malformed |
| **Mode B: 0x80040400 COM error** | All remaining records (~2066) | `COMException (0x80040400): QuickBooks found an error when parsing the provided XML text stream` | XML parsing fails at the COM level before QB even processes the request |

**Key insight**: The 0x80040400 error is NOT a "field not supported" error in this case. It's a **malformed XML** error. QB can't even parse the XML stream — it never gets to field validation.

---

## 2. EVIDENCE FROM IMPORT LOG

### Mode A — "null" errors (first 6 accounts)
```
[05:22:15 WRN]   ✗ Skipped 'Personal Bk Acct': null
[05:22:16 WRN]   ✗ Skipped 'Educators Credit Union': null
[05:22:16 WRN]   ✗ Skipped 'ED SAVING ACCT': null
[05:22:16 WRN]   ✗ Skipped 'ADJUSTMENT ACCT': null
[05:22:16 WRN]   ✗ Skipped '1 CASH REGISTER SALES': null
[05:22:16 WRN]   ✗ Skipped 'zz old bank header': null
```

These accounts have simple Fields (Name, IsActive, AccountType only). The "null" error means `statusMessage` was null in the response — QB returned a response but with no status message. This suggests the XML was accepted but the response parsing failed, OR the request was silently rejected.

### Mode B — 0x80040400 (all remaining records)
```
[05:22:16 ERR] COMException (0x80040400): QuickBooks found an error when parsing the provided XML text stream.
[05:22:16 WRN]   ✗ Skipped 'CASH ACCOUNT PAYROLL ADV': QBXML request failed...
```

These accounts have ParentRef or other nested objects. The COM error fires BEFORE QB processes the request — the XML itself is unparseable.

### Final tally
```
[05:23:55 INF]   IMPORT COMPLETE: 0/2072 records imported successfully
[05:23:55 WRN]   2072 records failed to import
[05:23:55 INF]   30 incompatible fields skipped (SDK 15.0 compatibility)
```

---

## 3. ROOT CAUSE ANALYSIS

### 3.1 The QBXML Being Generated

Looking at `DataImporter.cs` line 623-654, the `BuildAddRequestXml` method constructs:

```csharp
var innerXml = new StringBuilder();
innerXml.AppendLine($"<{addReqType}>");
innerXml.AppendLine($"  <{addElemType}>");
innerXml.Append(fieldsXml);
innerXml.AppendLine($"  </{addElemType}>");
innerXml.AppendLine($"</{addReqType}>");

return QBConnectionManager.BuildQBXMLRequest(innerXml.ToString(), _effectiveSDKVersion);
```

And `BuildQBXMLRequest` (QBConnectionManager.cs line 224-233):

```csharp
public static string BuildQBXMLRequest(string innerRequestXml, string sdkVersion = "16.0")
{
    return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<?qbxml version=""{sdkVersion}""?>
<QBXML>
  <QBXMLMsgsRq onError=""continueOnError"">
    {innerRequestXml}
  </QBXMLMsgsRq>
</QBXML>";
}
```

### 3.2 Likely Issues with Generated XML

**ISSUE 1: AccountAdd requires AccountType BEFORE Name in QBXML**

Per QBXML SDK documentation, element ORDER matters. QuickBooks has strict XML schema validation. The `AccountAddRq` requires elements in a specific order:
- `Name` must come first
- `IsActive` is optional
- `AccountType` is REQUIRED and must appear in the correct position

But `BuildFieldsXml` iterates over `JObject.Properties()` which follows JSON property insertion order. If the JSON has `{"Name": "...", "IsActive": "true", "AccountType": "Bank"}`, the XML would be:
```xml
<AccountAdd>
    <Name>Personal Bk Acct</Name>
    <IsActive>true</IsActive>
    <AccountType>Bank</AccountType>
</AccountAdd>
```

This might be correct for AccountAdd, but for other entity types the field ordering could be wrong.

**ISSUE 2: ParentRef handling for sub-accounts**

Accounts with `ParentRef` (like "CASH ACCOUNT PAYROLL ADV" under "zz old bank header") generate:
```xml
<AccountAdd>
    <Name>zz old bank header:CASH ACCOUNT PAYROLL A</Name>
    <IsActive>true</IsActive>
    <AccountType>Bank</AccountType>
    <ParentRef>
      <FullName>zz old bank header</FullName>
    </ParentRef>
</AccountAdd>
```

Problems:
1. The `Name` field contains the FULL hierarchical name ("zz old bank header:CASH ACCOUNT PAYROLL A") but for AccountAdd, `Name` should be just the LEAF name ("CASH ACCOUNT PAYROLL ADV")
2. The Name is truncated to 41 chars (QB 2021 limit) — "zz old bank header:CASH ACCOUNT PAYROLL A" — cutting off the actual name
3. ParentRef must reference an account that ALREADY EXISTS in QB 2021 — but the parent "zz old bank header" failed to import (Mode A error)

**ISSUE 3: The "null" error for simple accounts**

The first 6 accounts (Personal Bk Acct, etc.) have no ParentRef and simple fields. They should work. The "null" error suggests:
- The QBXML response was received but `statusMessage` attribute was null
- OR the response XML structure doesn't match what the parser expects
- OR the account already exists in QB 2021 (status code 3100) but the error message is null

**ISSUE 4: `BuildQBXMLRequest` default parameter is "16.0"**

In `QBConnectionManager.cs` line 224:
```csharp
public static string BuildQBXMLRequest(string innerRequestXml, string sdkVersion = "16.0")
```

The DEFAULT is "16.0"! While `DataImporter` passes `_effectiveSDKVersion` (which is "15.0"), any code path that calls `BuildQBXMLRequest` without explicitly passing the version will use 16.0 — which QB 2021 cannot parse.

Similarly, `BuildQueryRequest` on line 238 also defaults to "16.0":
```csharp
public static string BuildQueryRequest(string queryType, string sdkVersion = "16.0", ...)
```

**This is a potential smoking gun** — if any query or request is built using the default parameter, it would generate `<?qbxml version="16.0"?>` which QB 2021 would reject with 0x80040400.

### 3.3 Field Ordering Problem (CRITICAL)

QBXML is **order-sensitive**. The SDK schema defines a strict element order for each request type. `BuildFieldsXml` iterates over JSON properties in insertion order, which may not match the required QBXML element order.

For example, `AccountAddRq` requires this order:
1. Name (required)
2. IsActive
3. ParentRef
4. AccountType (required)
5. AccountNumber
6. BankNumber
7. Desc
8. OpenBalance / OpenBalanceDate

But the JSON might have them in any order. If `AccountType` appears before `ParentRef` when the schema requires the opposite, QB will reject the XML.

### 3.4 The `FullName` vs `Name` Problem

In the ExcludedFields set (line 109), `FullName` is excluded:
```csharp
"ListID", "TxnID", "TimeCreated", "TimeModified", "EditSequence",
"TxnNumber", "Balance", "TotalBalance", "Subtotal", "BalanceRemaining",
"IsPaid", "ExternalGUID", "FullName", "OpenBalance",
```

But the `Name` field in the transformed data contains the FULL hierarchical name (e.g., "zz old bank header:CASH ACCOUNT PAYROLL A") instead of just the leaf name. For sub-accounts/sub-items, the `Name` element in QBXML should be the leaf name only, with `ParentRef` providing the hierarchy.

---

## 4. COMPARISON: EARLIER LOG vs LATEST LOG

Both log files (`QB-TimeWarp-20260519.log` and `QB-TimeWarp-20260519_001.log`) show **identical 100% failure**:

| Log File | Time | Result |
|---|---|---|
| `QB-TimeWarp-20260519.log` (earlier) | ~03:09 AM | 0 succeeded across all entity types |
| `QB-TimeWarp-20260519_001.log` (later) | ~05:22 AM | 0/2072 succeeded |
| `import_log.txt` (latest) | ~05:22 AM | 0/2072 succeeded |

**There was never a partially working run in these logs.** The regression may have occurred in a previous session/context that we don't have logs for.

---

## 5. QBXML SDK 15.0 REQUIREMENTS (from Intuit Documentation)

### CustomerAddRq
- **Required**: `Name` (max 41 chars in SDK 15.0)
- **Optional**: CompanyName, FirstName, LastName, BillAddress, Phone, Email, TermsRef, etc.
- **Element order**: Strict — Name must come first

### VendorAddRq  
- **Required**: `Name` (max 41 chars)
- **Optional**: CompanyName, VendorAddress, Phone, TermsRef, etc.

### AccountAddRq
- **Required**: `Name` (max 41 chars), `AccountType` (enum: Bank, AccountsReceivable, OtherCurrentAsset, etc.)
- **Element order**: Name → IsActive → ParentRef → AccountType → ...
- **For sub-accounts**: Name = leaf name only, ParentRef = parent's FullName

### General Rules
- QBXML version in `<?qbxml version="X.X"?>` must be ≤ 15.0 for QB 2021
- Element ordering is STRICT — must match the XSD schema
- Character encoding: ISO-8859-1 only (non-ISO chars → question marks)
- Field lengths are shorter in SDK 15.0 vs 16.0 (Name: 41 vs 209, etc.)

---

## 6. PRIORITIZED FIX LIST

### P0 — Must Fix (blocks everything)

1. **Verify QBXML version in ALL request paths** — Ensure no code path uses the default "16.0" parameter. Search for all calls to `BuildQBXMLRequest` and `BuildQueryRequest` and verify they pass "15.0" explicitly.

2. **Fix element ordering in BuildFieldsXml** — QBXML requires strict element ordering. The current implementation iterates JSON properties in arbitrary order. Need to define the correct element order for each entity type and emit fields in that order.

3. **Fix Name field for hierarchical entities** — For sub-accounts, sub-items, sub-customers (jobs): the `Name` field must contain only the leaf name, not the full colon-separated path. The hierarchy is expressed via `ParentRef`.

### P1 — Important

4. **Debug the "null" error for simple accounts** — Add logging to capture the actual QBXML request being sent and the raw response for the first few records. This will reveal exactly what's wrong.

5. **Add QBXML request/response logging** — Before sending any request, log the full XML. On failure, log the full response. This is essential for debugging.

6. **Handle dependency ordering for ParentRef** — Parent accounts must be imported before child accounts. The current import order processes all accounts together but doesn't sort by hierarchy depth.

### P2 — Nice to Have

7. **Add a minimal test** — Construct a hardcoded minimal `AccountAddRq` and send it to QB 2021 to verify the connection and basic XML processing works. This isolates connection issues from data issues.

8. **Validate XML before sending** — Parse the generated XML with an XML parser before sending to QB to catch malformation early.

---

## 7. RECOMMENDED DEBUGGING APPROACH

### Step 1: Add XML Logging
Add `Log.Debug("QBXML Request:\n{XML}", requestXml)` before `ProcessRequest` calls to see exactly what's being sent.

### Step 2: Minimal Test
Hardcode and send this minimal request:
```xml
<?xml version="1.0" encoding="utf-8"?>
<?qbxml version="15.0"?>
<QBXML>
  <QBXMLMsgsRq onError="continueOnError">
    <AccountAddRq>
      <AccountAdd>
        <Name>_Test_Account_001</Name>
        <AccountType>Bank</AccountType>
      </AccountAdd>
    </AccountAddRq>
  </QBXMLMsgsRq>
</QBXML>
```

If this works → problem is in data/field generation.  
If this fails → problem is in connection/session/QB state.

### Step 3: Fix Element Ordering
Define ordered field lists per entity type. Emit fields in schema order.

### Step 4: Fix Name/FullName Handling
For entities with ParentRef, use only the leaf name in the `Name` element.

---

## 8. CODE LOCATIONS FOR FIXES

| File | Line | What to Fix |
|---|---|---|
| `Services/QBConnectionManager.cs` | 224 | Change default `sdkVersion` from "16.0" to "15.0" |
| `Services/QBConnectionManager.cs` | 238 | Change default `sdkVersion` from "16.0" to "15.0" |
| `Services/DataImporter.cs` | 697-768 | `BuildFieldsXml` — add element ordering logic |
| `Services/DataImporter.cs` | 623-654 | `BuildAddRequestXml` — add Name/FullName leaf extraction |
| `Services/DataImporter.cs` | 330-442 | `ImportSingleEntity` — add request XML logging |
| `Services/DataTransformer.cs` | varies | Ensure Name field contains leaf name for hierarchical entities |

---

## 9. WHAT THE API DOCS SAY vs WHAT WE DO

| Requirement | API Docs | Our Code | Status |
|---|---|---|---|
| QBXML version ≤ 15.0 | Required for QB 2021 | `_effectiveSDKVersion` = "15.0" but defaults are "16.0" | ⚠️ Risk |
| Element ordering | Strict per XSD | JSON property iteration order (arbitrary) | 🔴 Broken |
| Name = leaf only for sub-entities | Required | Name contains full path with colons | 🔴 Broken |
| AccountType required for AccountAdd | Required | Present in data | ✅ OK |
| Field lengths ≤ SDK 15.0 limits | Required | `EnforceFieldLength` handles this | ✅ OK |
| SDK 16-only fields excluded | Required | `ExcludedFields` + `SDK16OnlyFields` | ✅ OK |
| ISO-8859-1 encoding | Required | Not explicitly enforced | ⚠️ Risk |

---

## 10. NEXT STEPS

1. Create a branch with diagnostic logging (log full QBXML requests on failure)
2. Add a `--test-minimal` flag that sends a hardcoded minimal request to verify connectivity
3. Fix the element ordering issue (define schema-ordered field lists)
4. Fix the Name/FullName leaf extraction for hierarchical entities
5. Fix the default SDK version parameters
6. Re-run import and analyze results
