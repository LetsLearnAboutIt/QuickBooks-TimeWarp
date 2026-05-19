using Serilog;

namespace QB_TimeWarp.Helpers
{
    /// <summary>
    /// Provides SDK version detection and compatibility checks for QuickBooks QBXML.
    /// QB 2021 uses SDK 15.0; QB 2023 uses SDK 16.0.
    /// The 0x80040400 COM error typically means a field or QBXML version is not supported.
    /// </summary>
    public static class QBSDKVersionHelper
    {
        /// <summary>
        /// Known SDK version mappings for QuickBooks Desktop versions.
        /// </summary>
        private static readonly Dictionary<string, string> QBVersionToSDK = new(StringComparer.OrdinalIgnoreCase)
        {
            ["2021"] = "15.0",
            ["2022"] = "16.0",
            ["2023"] = "16.0",
            ["2024"] = "16.0",
        };

        /// <summary>
        /// The maximum QBXML version supported by QB 2021 (SDK 15.0).
        /// </summary>
        public const string QB2021_MAX_SDK_VERSION = "15.0";

        /// <summary>
        /// The QBXML version used by QB 2023 (SDK 16.0).
        /// </summary>
        public const string QB2023_SDK_VERSION = "16.0";

        /// <summary>
        /// Fields introduced in SDK 16.0 that do NOT exist in SDK 15.0 (QB 2021).
        /// Sending these in QBXML to QB 2021 will cause 0x80040400 errors.
        /// </summary>
        public static readonly HashSet<string> SDK16OnlyFields = new(StringComparer.OrdinalIgnoreCase)
        {
            // Customer fields added in 2023
            "ExternalGUID",
            "TaxRegistrationNumber",
            "CurrencyRef",
            "PreferredDeliveryMethod",

            // Employee fields added in 2023
            "EmployeeType",
            "EmployeePayrollInfo",
            "BillingRateRef",
            "EmployeePayrollInfo.ClearEarnings",
            "EmployeePayrollInfo.SickHours",
            "EmployeePayrollInfo.VacationHours",

            // Item fields added/changed in 2023
            "UnitOfMeasureSetRef",
            "ForceUOMChange",
            "ClassRef",  // on items specifically — not on transactions

            // Invoice fields added in 2023
            "ExchangeRate",
            "LinkToTxnID",

            // General fields not in 2021
            "SubscriptionPaymentStatus",
            "DeliveryInfo",
            "TaxLineRef",
        };

        /// <summary>
        /// Entity types / request types that exist in SDK 16.0 but NOT in SDK 15.0.
        /// These requests will fail entirely against QB 2021.
        /// </summary>
        public static readonly HashSet<string> SDK16OnlyEntityTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "TransferInventory",
            "TransferInventoryQuery",
            "TransferInventoryAdd",
        };

        /// <summary>
        /// Fields that have different max lengths between SDK 15.0 and 16.0.
        /// Key = field name, Value = (SDK15MaxLength, SDK16MaxLength)
        /// </summary>
        public static readonly Dictionary<string, (int SDK15Max, int SDK16Max)> FieldLengthDifferences = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"]            = (41, 209),
            ["CompanyName"]     = (41, 209),
            ["FirstName"]       = (25, 100),
            ["LastName"]        = (25, 100),
            ["MiddleName"]      = (5, 100),
            ["Salutation"]      = (15, 100),
            ["JobTitle"]        = (41, 100),
            ["Phone"]           = (21, 21),
            ["AltPhone"]        = (21, 21),
            ["Fax"]             = (21, 21),
            ["Email"]           = (1023, 1023),
            ["Contact"]         = (41, 100),
            ["AltContact"]      = (41, 100),
            ["AccountNumber"]   = (99, 99),
            ["Addr1"]           = (41, 500),
            ["Addr2"]           = (41, 500),
            ["Addr3"]           = (41, 500),
            ["Addr4"]           = (41, 500),
            ["Addr5"]           = (41, 500),
            ["City"]            = (31, 255),
            ["State"]           = (21, 255),
            ["PostalCode"]      = (13, 30),
            ["Country"]         = (31, 255),
            ["Note"]            = (4095, 4095),
            ["Memo"]            = (4095, 4095),
            ["Desc"]            = (4095, 4095),
            ["Description"]     = (4095, 4095),
        };

        /// <summary>
        /// Payroll-related field names that need special handling in QB 2021.
        /// QB 2021 uses a simpler payroll item structure than QB 2023.
        /// </summary>
        public static readonly HashSet<string> PayrollFieldsToSimplify = new(StringComparer.OrdinalIgnoreCase)
        {
            "PayrollItemWageRef",
            "PayrollItemNonWageRef",
            "PayrollInfo",
            "Earnings",
            "EmployeePayrollInfo",
            "ClearEarnings",
            "SickHours",
            "VacationHours",
            "AdjustPercentage",
            "AdjustRelativeTo",
        };

        /// <summary>
        /// Returns the appropriate SDK version string for the given target QB version.
        /// </summary>
        public static string GetSDKVersionForQB(string qbVersion)
        {
            if (QBVersionToSDK.TryGetValue(qbVersion, out var sdkVersion))
                return sdkVersion;

            // Default to 15.0 for safety (oldest supported)
            Log.Warning("Unknown QB version '{QBVersion}', defaulting to SDK 15.0 for safety", qbVersion);
            return "15.0";
        }

        /// <summary>
        /// Determines if a given SDK version string is QB 2021 compatible (15.0 or lower).
        /// </summary>
        public static bool IsQB2021Compatible(string sdkVersion)
        {
            if (double.TryParse(sdkVersion, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var version))
            {
                return version <= 15.0;
            }
            return false;
        }

        /// <summary>
        /// Checks if a field is supported in QB 2021 (SDK 15.0).
        /// Returns false if the field only exists in SDK 16.0+.
        /// </summary>
        public static bool IsFieldSupportedInQB2021(string fieldName)
        {
            return !SDK16OnlyFields.Contains(fieldName);
        }

        /// <summary>
        /// Gets the max length for a field in SDK 15.0 (QB 2021).
        /// Returns null if no specific limit is known (use default).
        /// </summary>
        public static int? GetQB2021MaxLength(string fieldName)
        {
            if (FieldLengthDifferences.TryGetValue(fieldName, out var lengths))
                return lengths.SDK15Max;
            return null;
        }

        /// <summary>
        /// Checks whether a field is a payroll-related field that needs simplification for QB 2021.
        /// </summary>
        public static bool IsPayrollFieldToSimplify(string fieldName)
        {
            return PayrollFieldsToSimplify.Contains(fieldName);
        }

        /// <summary>
        /// Returns the safe QBXML version to use when targeting QB 2021.
        /// Always returns "15.0" to ensure compatibility.
        /// </summary>
        public static string GetSafeExportVersion()
        {
            return QB2021_MAX_SDK_VERSION;
        }

        /// <summary>
        /// Detects the QB SDK version from a QBXML response by checking for version indicators.
        /// </summary>
        public static string DetectSDKVersionFromResponse(string qbxmlResponse)
        {
            try
            {
                // Check for version in the XML processing instruction
                if (qbxmlResponse.Contains("version=\"16.0\""))
                    return "16.0";
                if (qbxmlResponse.Contains("version=\"15.0\""))
                    return "15.0";
                if (qbxmlResponse.Contains("version=\"14.0\""))
                    return "14.0";
                if (qbxmlResponse.Contains("version=\"13.0\""))
                    return "13.0";
            }
            catch (Exception ex)
            {
                Log.Debug("Could not detect SDK version from response: {Message}", ex.Message);
            }

            return "15.0"; // Safe default
        }

        /// <summary>
        /// Attempts to detect the QB SDK version by sending a HostQuery request.
        /// This should be called after connecting to QuickBooks.
        /// Returns the detected SDK version string.
        /// </summary>
        public static string BuildHostQueryRequest()
        {
            return @"<?xml version=""1.0"" encoding=""utf-8""?>
<?qbxml version=""15.0""?>
<QBXML>
  <QBXMLMsgsRq onError=""continueOnError"">
    <HostQueryRq>
    </HostQueryRq>
  </QBXMLMsgsRq>
</QBXML>";
        }

        /// <summary>
        /// Parses the HostQuery response to extract the supported SDK versions.
        /// </summary>
        public static (string MaxVersion, List<string> SupportedVersions) ParseHostQueryResponse(string response)
        {
            var supportedVersions = new List<string>();
            string maxVersion = "15.0";

            try
            {
                var doc = System.Xml.Linq.XDocument.Parse(response);
                var hostRet = doc.Descendants("HostRet").FirstOrDefault();

                if (hostRet != null)
                {
                    foreach (var versionElement in hostRet.Elements("SupportedQBXMLVersion"))
                    {
                        var version = versionElement.Value;
                        supportedVersions.Add(version);

                        if (double.TryParse(version, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var v) &&
                            double.TryParse(maxVersion, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var max) &&
                            v > max)
                        {
                            maxVersion = version;
                        }
                    }
                }

                Log.Information("QB SDK version detection: max={MaxVersion}, supported=[{Versions}]",
                    maxVersion, string.Join(", ", supportedVersions));
            }
            catch (Exception ex)
            {
                Log.Warning("Could not parse HostQuery response for version detection: {Message}", ex.Message);
            }

            return (maxVersion, supportedVersions);
        }

        /// <summary>
        /// Determines the effective SDK version to use: the minimum of the target
        /// and the max supported by the QB installation.
        /// </summary>
        public static string DetermineEffectiveSDKVersion(string targetVersion, string maxSupportedVersion)
        {
            if (double.TryParse(targetVersion, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var target) &&
                double.TryParse(maxSupportedVersion, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var maxSupported))
            {
                var effective = Math.Min(target, maxSupported);
                var effectiveStr = effective.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);

                if (effectiveStr != targetVersion)
                {
                    Log.Warning("Adjusted SDK version from {Target} to {Effective} based on QB capabilities",
                        targetVersion, effectiveStr);
                }
                else
                {
                    Log.Information("Using SDK version {Version} (compatible with QB installation)", effectiveStr);
                }

                return effectiveStr;
            }

            return "15.0"; // Safe default
        }

        /// <summary>
        /// Checks if a COM error code indicates an unsupported QBXML field/version.
        /// 0x80040400 is the classic "element not supported" error.
        /// </summary>
        public static bool IsUnsupportedFieldError(string? errorCode)
        {
            if (string.IsNullOrEmpty(errorCode)) return false;

            // Known QBXML error codes for unsupported elements
            return errorCode switch
            {
                "3250" => true,  // Unsupported element in request
                "3260" => true,  // Element not valid for this version
                "3270" => true,  // Unsupported QBXML version
                "3100" => false, // Name already exists (not a version issue)
                "3200" => false, // Object not found (not a version issue)
                _ => false
            };
        }

        /// <summary>
        /// Checks if a COM HRESULT is the 0x80040400 "element not supported" error.
        /// </summary>
        public static bool IsUnsupportedElementCOMError(int hResult)
        {
            // 0x80040400 = -2147220480 in signed int32
            return hResult == unchecked((int)0x80040400);
        }

        /// <summary>
        /// Provides a human-readable explanation for common QBXML error codes.
        /// </summary>
        public static string ExplainErrorCode(string? errorCode)
        {
            if (string.IsNullOrEmpty(errorCode)) return "Unknown error";

            return errorCode switch
            {
                "0" => "Success",
                "1" => "Some data not returned in query",
                "500" => "Object already exists",
                "3100" => "Name already in use",
                "3120" => "Object not found",
                "3140" => "Cannot modify object",
                "3170" => "Object specified is in use",
                "3175" => "Name too long",
                "3180" => "Cannot delete object",
                "3200" => "Referenced object not found",
                "3250" => "Unsupported QBXML element (SDK version mismatch — field not supported in this QB version)",
                "3260" => "Element not valid for this QBXML version",
                "3270" => "Unsupported QBXML version requested",
                _ => $"QBXML error code {errorCode}"
            };
        }
    }
}
