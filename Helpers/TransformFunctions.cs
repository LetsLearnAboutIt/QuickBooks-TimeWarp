using Serilog;

namespace QB_TimeWarp.Helpers
{
    /// <summary>
    /// Built-in transformation functions referenced by FieldMappings.json.
    /// These handle common differences between QuickBooks 2023 and 2021 data formats.
    /// </summary>
    public static class TransformFunctions
    {
        /// <summary>
        /// Maps QB 2023 job status values to QB 2021 equivalents.
        /// QB 2023 may have added new statuses not recognized by 2021.
        /// </summary>
        public static string MapJobStatus(string value)
        {
            return value switch
            {
                "Awarded" => "Awarded",
                "Closed" => "Closed",
                "InProgress" => "InProgress",
                "None" => "None",
                "NotAwarded" => "NotAwarded",
                "Pending" => "Pending",
                // New in QB 2023, mapped to closest QB 2021 equivalent
                "InNegotiation" => "Pending",
                "OnHold" => "Pending",
                _ => "None"
            };
        }

        /// <summary>
        /// Maps newer payment method types (e.g., Venmo, Zelle) to QB 2021 equivalents.
        /// </summary>
        public static string MapPaymentMethod(string value)
        {
            return value switch
            {
                "Venmo" or "Zelle" or "CashApp" or "ApplePay" or "GooglePay" => "Other",
                "Cash" => "Cash",
                "Check" => "Check",
                "CreditCard" => "CreditCard",
                "DebitCard" => "DebitCard",
                "ECheck" => "ECheck",
                "AmEx" => "AmEx",
                "Discover" => "Discover",
                "MasterCard" => "MasterCard",
                "Visa" => "Visa",
                _ => "Other"
            };
        }

        /// <summary>
        /// Truncates a string to the specified maximum length, adding an ellipsis indicator.
        /// </summary>
        public static string TruncateString(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
                return value;

            Log.Debug("Truncating string from {Original} to {Max} chars", value.Length, maxLength);
            return value.Substring(0, maxLength);
        }

        /// <summary>
        /// Formats a date string to the standard QBXML date format (yyyy-MM-dd).
        /// </summary>
        public static string FormatDate(string value)
        {
            if (DateTime.TryParse(value, out var date))
            {
                return date.ToString("yyyy-MM-dd");
            }
            return value;
        }

        /// <summary>
        /// Strips timezone information from datetime strings (QB 2023 may include TZ, 2021 doesn't).
        /// </summary>
        public static string StripTimezone(string value)
        {
            if (DateTime.TryParse(value, out var date))
            {
                return date.ToString("yyyy-MM-ddTHH:mm:ss");
            }
            return value;
        }

        /// <summary>
        /// Maps QB 2023 account types to QB 2021 equivalents.
        /// </summary>
        public static string MapAccountType(string value)
        {
            // Most account types are the same; this handles any new types added in 2023
            var validTypes = new HashSet<string>
            {
                "AccountsPayable", "AccountsReceivable", "Bank", "CostOfGoodsSold",
                "CreditCard", "Equity", "Expense", "FixedAsset", "Income",
                "LongTermLiability", "NonPosting", "OtherAsset", "OtherCurrentAsset",
                "OtherCurrentLiability", "OtherExpense", "OtherIncome"
            };

            if (validTypes.Contains(value))
                return value;

            Log.Warning("Unknown account type '{Value}', mapping to 'OtherExpense'", value);
            return "OtherExpense";
        }

        /// <summary>
        /// Cleans phone number formatting for QB 2021 compatibility.
        /// </summary>
        public static string CleanPhoneNumber(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;

            // Remove any extensions beyond what QB 2021 supports (max 21 chars)
            if (value.Length > 21)
                return value.Substring(0, 21);

            return value;
        }

        /// <summary>
        /// Normalizes boolean values to QB-compatible format.
        /// </summary>
        public static string NormalizeBoolean(string value)
        {
            if (string.IsNullOrEmpty(value)) return "false";

            return value.ToLowerInvariant() switch
            {
                "true" or "1" or "yes" or "y" => "true",
                "false" or "0" or "no" or "n" => "false",
                _ => "false"
            };
        }

        /// <summary>
        /// Formats decimal/amount values to standard format (2 decimal places).
        /// </summary>
        public static string FormatAmount(string value)
        {
            if (decimal.TryParse(value, out var amount))
            {
                return amount.ToString("F2");
            }
            return value;
        }
    }
}
