using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using QB_TimeWarp.Models;
using Serilog;

namespace QB_TimeWarp.Services
{
    /// <summary>
    /// Validates migrated data by comparing source (QB 2023) and target (QB 2021) data.
    /// Performs field-by-field comparison, entity count verification, and financial reconciliation.
    /// </summary>
    public class DataValidator
    {
        private readonly QBConnectionManager _sourceConnection;
        private readonly QBConnectionManager _targetConnection;
        private readonly ValidationConfig _validationConfig;
        private readonly string _outputDirectory;

        public DataValidator(
            QBConnectionManager sourceConnection,
            QBConnectionManager targetConnection,
            ValidationConfig validationConfig,
            string outputDirectory)
        {
            _sourceConnection = sourceConnection;
            _targetConnection = targetConnection;
            _validationConfig = validationConfig;
            _outputDirectory = outputDirectory;
        }

        /// <summary>
        /// Runs all configured validation checks and produces a comprehensive report.
        /// </summary>
        public ValidationReport ValidateAll(
            Dictionary<string, ExportedEntitySet> sourceData,
            Dictionary<string, ExportedEntitySet> importedData)
        {
            Log.Information("═══════════════════════════════════════════════════════");
            Log.Information("  STARTING DATA VALIDATION");
            Log.Information("═══════════════════════════════════════════════════════");

            var report = new ValidationReport { ValidatedAt = DateTime.UtcNow };

            // 1. Entity count verification
            if (_validationConfig.EnableEntityCountVerification)
            {
                Log.Information("Running entity count verification...");
                report.EntityCountComparisons = VerifyEntityCounts(sourceData, importedData);
            }

            // 2. Field-by-field comparison
            if (_validationConfig.EnableFieldByFieldComparison)
            {
                Log.Information("Running field-by-field comparison...");
                report.FieldDiscrepancies = CompareFieldByField(sourceData, importedData);
            }

            // 3. Financial reconciliation
            if (_validationConfig.EnableFinancialReconciliation)
            {
                Log.Information("Running financial reconciliation...");
                report.FinancialReconciliation = ReconcileFinancials(sourceData, importedData);
            }

            // Calculate totals
            report.TotalDiscrepancies = report.FieldDiscrepancies.Count;
            report.CriticalDiscrepancies = report.FieldDiscrepancies
                .Count(d => d.Severity == DiscrepancySeverity.Critical);
            report.WarningDiscrepancies = report.FieldDiscrepancies
                .Count(d => d.Severity == DiscrepancySeverity.Warning);

            report.IsValid = report.CriticalDiscrepancies == 0
                && report.EntityCountComparisons.All(c => c.Matches)
                && (report.FinancialReconciliation?.IsReconciled ?? true);

            report.Summary = BuildSummary(report);

            // Save report
            SaveReport(report);

            Log.Information("═══════════════════════════════════════════════════════");
            Log.Information("  VALIDATION {Status}: {Critical} critical, {Warning} warnings",
                report.IsValid ? "PASSED" : "FAILED",
                report.CriticalDiscrepancies, report.WarningDiscrepancies);
            Log.Information("═══════════════════════════════════════════════════════");

            return report;
        }

        /// <summary>
        /// Verifies entity counts match between source and imported data.
        /// </summary>
        private List<EntityCountComparison> VerifyEntityCounts(
            Dictionary<string, ExportedEntitySet> sourceData,
            Dictionary<string, ExportedEntitySet> importedData)
        {
            var comparisons = new List<EntityCountComparison>();

            var allTypes = sourceData.Keys.Union(importedData.Keys).Distinct();

            foreach (var entityType in allTypes)
            {
                var sourceCount = sourceData.ContainsKey(entityType)
                    ? sourceData[entityType].TotalCount : 0;
                var targetCount = importedData.ContainsKey(entityType)
                    ? importedData[entityType].TotalCount : 0;

                var comparison = new EntityCountComparison
                {
                    EntityType = entityType,
                    SourceCount = sourceCount,
                    TargetCount = targetCount
                };

                if (!comparison.Matches)
                {
                    comparison.Notes = $"Count mismatch: source has {sourceCount}, target has {targetCount} " +
                        $"(difference: {comparison.Difference})";
                    Log.Warning("  Count mismatch for {EntityType}: {Source} vs {Target}",
                        entityType, sourceCount, targetCount);
                }
                else
                {
                    Log.Information("  ✓ {EntityType}: {Count} records match", entityType, sourceCount);
                }

                comparisons.Add(comparison);
            }

            return comparisons;
        }

        /// <summary>
        /// Performs field-by-field comparison between source and imported entities.
        /// Matches entities by Name/FullName since ListIDs will differ.
        /// </summary>
        private List<FieldComparisonResult> CompareFieldByField(
            Dictionary<string, ExportedEntitySet> sourceData,
            Dictionary<string, ExportedEntitySet> importedData)
        {
            var discrepancies = new List<FieldComparisonResult>();
            int totalFieldsCompared = 0;

            foreach (var entityType in sourceData.Keys)
            {
                if (!importedData.ContainsKey(entityType)) continue;

                var sourceEntities = sourceData[entityType].Entities;
                var targetEntities = importedData[entityType].Entities
                    .ToDictionary(e => e.FullName ?? e.Name, e => e, StringComparer.OrdinalIgnoreCase);

                foreach (var sourceEntity in sourceEntities)
                {
                    var sourceName = sourceEntity.FullName ?? sourceEntity.Name;
                    if (!targetEntities.TryGetValue(sourceName, out var targetEntity))
                    {
                        discrepancies.Add(new FieldComparisonResult
                        {
                            EntityType = entityType,
                            EntityIdentifier = sourceName,
                            FieldName = "_Entity",
                            SourceValue = "exists",
                            TargetValue = "missing",
                            Severity = DiscrepancySeverity.Critical,
                            Reason = "Entity exists in source but not found in target"
                        });
                        continue;
                    }

                    // Compare each field in the source entity
                    var fieldDiscrepancies = CompareJObjects(
                        sourceEntity.Fields, targetEntity.Fields,
                        entityType, sourceName, "");

                    totalFieldsCompared += CountFields(sourceEntity.Fields);
                    discrepancies.AddRange(fieldDiscrepancies);
                }
            }

            Log.Information("  Compared {Count} fields total, found {Discrepancies} discrepancies",
                totalFieldsCompared, discrepancies.Count);

            return discrepancies;
        }

        /// <summary>
        /// Recursively compares two JObjects field by field.
        /// </summary>
        private List<FieldComparisonResult> CompareJObjects(
            JObject source, JObject target,
            string entityType, string entityName, string prefix)
        {
            var results = new List<FieldComparisonResult>();

            foreach (var prop in source.Properties())
            {
                var fieldName = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";

                // Skip system/read-only fields that won't match
                if (IsSkippableField(prop.Name)) continue;

                var targetValue = target[prop.Name];

                if (prop.Value is JObject sourceNested)
                {
                    if (targetValue is JObject targetNested)
                    {
                        results.AddRange(CompareJObjects(sourceNested, targetNested,
                            entityType, entityName, fieldName));
                    }
                    else if (targetValue == null)
                    {
                        results.Add(new FieldComparisonResult
                        {
                            EntityType = entityType,
                            EntityIdentifier = entityName,
                            FieldName = fieldName,
                            SourceValue = sourceNested.ToString(Formatting.None),
                            TargetValue = null,
                            Severity = DiscrepancySeverity.Warning,
                            Reason = "Nested field group missing in target"
                        });
                    }
                }
                else
                {
                    var sourceVal = prop.Value?.ToString() ?? "";
                    var targetVal = targetValue?.ToString() ?? "";

                    if (!ValuesMatch(sourceVal, targetVal))
                    {
                        var severity = DetermineSeverity(fieldName, sourceVal, targetVal);
                        results.Add(new FieldComparisonResult
                        {
                            EntityType = entityType,
                            EntityIdentifier = entityName,
                            FieldName = fieldName,
                            SourceValue = sourceVal,
                            TargetValue = targetVal,
                            Severity = severity,
                            Reason = DetermineReason(fieldName, sourceVal, targetVal)
                        });
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Compares two values with tolerance for numeric values and format differences.
        /// </summary>
        private bool ValuesMatch(string sourceVal, string targetVal)
        {
            if (sourceVal == targetVal) return true;
            if (string.IsNullOrEmpty(sourceVal) && string.IsNullOrEmpty(targetVal)) return true;

            // Try numeric comparison with tolerance
            if (decimal.TryParse(sourceVal, out var sourceNum) &&
                decimal.TryParse(targetVal, out var targetNum))
            {
                return Math.Abs(sourceNum - targetNum) <= _validationConfig.ToleranceAmount;
            }

            // Try date comparison (format might differ)
            if (DateTime.TryParse(sourceVal, out var sourceDate) &&
                DateTime.TryParse(targetVal, out var targetDate))
            {
                return sourceDate.Date == targetDate.Date;
            }

            // Case-insensitive string comparison
            return string.Equals(sourceVal, targetVal, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Performs financial reconciliation: account balances, AR/AP totals, transaction sums.
        /// </summary>
        private FinancialReconciliation ReconcileFinancials(
            Dictionary<string, ExportedEntitySet> sourceData,
            Dictionary<string, ExportedEntitySet> importedData)
        {
            var reconciliation = new FinancialReconciliation();

            // Compare account balances
            if (sourceData.ContainsKey("Accounts") && importedData.ContainsKey("Accounts"))
            {
                reconciliation.AccountBalances = CompareAccountBalances(
                    sourceData["Accounts"], importedData["Accounts"]);
            }

            // Calculate AR summary
            reconciliation.ARSummary = CalculateBalanceSummary(
                sourceData, importedData, "AccountsReceivable", "AR");

            // Calculate AP summary
            reconciliation.APSummary = CalculateBalanceSummary(
                sourceData, importedData, "AccountsPayable", "AP");

            // Compare transaction totals by type
            reconciliation.TransactionTotals = CompareTransactionTotals(sourceData, importedData);

            // Determine if reconciled
            reconciliation.IsReconciled =
                reconciliation.AccountBalances.All(ab => ab.IsWithinTolerance(_validationConfig.ToleranceAmount)) &&
                reconciliation.ARSummary.IsWithinTolerance(_validationConfig.ToleranceAmount) &&
                reconciliation.APSummary.IsWithinTolerance(_validationConfig.ToleranceAmount) &&
                reconciliation.TransactionTotals.All(tt => tt.IsWithinTolerance(_validationConfig.ToleranceAmount));

            if (reconciliation.IsReconciled)
            {
                Log.Information("  ✓ Financial reconciliation passed");
            }
            else
            {
                Log.Warning("  ✗ Financial reconciliation found discrepancies");
            }

            return reconciliation;
        }

        /// <summary>
        /// Compares account balances between source and target.
        /// </summary>
        private List<AccountBalanceComparison> CompareAccountBalances(
            ExportedEntitySet sourceAccounts, ExportedEntitySet targetAccounts)
        {
            var comparisons = new List<AccountBalanceComparison>();

            var targetLookup = targetAccounts.Entities
                .ToDictionary(e => e.FullName ?? e.Name, e => e, StringComparer.OrdinalIgnoreCase);

            foreach (var sourceAcct in sourceAccounts.Entities)
            {
                var name = sourceAcct.FullName ?? sourceAcct.Name;
                var sourceBalance = GetDecimalField(sourceAcct.Fields, "Balance");
                var accountType = sourceAcct.Fields["AccountType"]?.ToString() ?? "Unknown";

                decimal targetBalance = 0;
                if (targetLookup.TryGetValue(name, out var targetAcct))
                {
                    targetBalance = GetDecimalField(targetAcct.Fields, "Balance");
                }

                comparisons.Add(new AccountBalanceComparison
                {
                    AccountName = name,
                    AccountType = accountType,
                    SourceBalance = sourceBalance,
                    TargetBalance = targetBalance
                });

                if (Math.Abs(sourceBalance - targetBalance) > _validationConfig.ToleranceAmount)
                {
                    Log.Warning("  Account balance mismatch: {Account} - Source: {Source}, Target: {Target}",
                        name, sourceBalance, targetBalance);
                }
            }

            return comparisons;
        }

        /// <summary>
        /// Calculates and compares AR or AP balance summaries.
        /// </summary>
        private BalanceSummary CalculateBalanceSummary(
            Dictionary<string, ExportedEntitySet> sourceData,
            Dictionary<string, ExportedEntitySet> importedData,
            string accountType, string category)
        {
            decimal sourceTotal = 0;
            decimal targetTotal = 0;

            if (sourceData.ContainsKey("Accounts"))
            {
                sourceTotal = sourceData["Accounts"].Entities
                    .Where(e => e.Fields["AccountType"]?.ToString() == accountType)
                    .Sum(e => GetDecimalField(e.Fields, "Balance"));
            }

            if (importedData.ContainsKey("Accounts"))
            {
                targetTotal = importedData["Accounts"].Entities
                    .Where(e => e.Fields["AccountType"]?.ToString() == accountType)
                    .Sum(e => GetDecimalField(e.Fields, "Balance"));
            }

            return new BalanceSummary
            {
                Category = category,
                SourceTotal = sourceTotal,
                TargetTotal = targetTotal
            };
        }

        /// <summary>
        /// Compares transaction totals by type between source and target.
        /// </summary>
        private List<TransactionTotalComparison> CompareTransactionTotals(
            Dictionary<string, ExportedEntitySet> sourceData,
            Dictionary<string, ExportedEntitySet> importedData)
        {
            var comparisons = new List<TransactionTotalComparison>();
            var txnTypes = new[] { "Invoices", "Bills", "Payments", "SalesReceipts",
                "PurchaseOrders", "JournalEntries", "CreditMemos", "Checks", "Deposits" };

            foreach (var txnType in txnTypes)
            {
                var sourceCount = 0;
                decimal sourceTotal = 0;
                var targetCount = 0;
                decimal targetTotal = 0;

                if (sourceData.ContainsKey(txnType))
                {
                    sourceCount = sourceData[txnType].TotalCount;
                    sourceTotal = sourceData[txnType].Entities
                        .Sum(e => GetTransactionAmount(e));
                }

                if (importedData.ContainsKey(txnType))
                {
                    targetCount = importedData[txnType].TotalCount;
                    targetTotal = importedData[txnType].Entities
                        .Sum(e => GetTransactionAmount(e));
                }

                comparisons.Add(new TransactionTotalComparison
                {
                    TransactionType = txnType,
                    SourceCount = sourceCount,
                    TargetCount = targetCount,
                    SourceTotalAmount = sourceTotal,
                    TargetTotalAmount = targetTotal
                });
            }

            return comparisons;
        }

        /// <summary>
        /// Extracts the total amount from a transaction entity.
        /// </summary>
        private static decimal GetTransactionAmount(QBEntity entity)
        {
            // Try common amount field names
            foreach (var fieldName in new[] { "TotalAmount", "Subtotal", "Amount", "OpenAmount" })
            {
                var amount = GetDecimalField(entity.Fields, fieldName);
                if (amount != 0) return amount;
            }

            // Sum line item amounts
            return entity.LineItems
                .Sum(li => GetDecimalField(li, "Amount"));
        }

        /// <summary>
        /// Safely extracts a decimal value from a JObject field.
        /// </summary>
        private static decimal GetDecimalField(JObject obj, string fieldName)
        {
            var token = obj[fieldName];
            if (token != null && decimal.TryParse(token.ToString(), out var value))
                return value;
            return 0;
        }

        private static bool IsSkippableField(string fieldName)
        {
            var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ListID", "TxnID", "TimeCreated", "TimeModified", "EditSequence",
                "TxnNumber", "ExternalGUID", "FullName", "DataExtRet"
            };
            return skip.Contains(fieldName);
        }

        private static DiscrepancySeverity DetermineSeverity(string fieldName, string sourceVal, string targetVal)
        {
            // Financial fields are critical
            if (fieldName.Contains("Amount") || fieldName.Contains("Balance") ||
                fieldName.Contains("Price") || fieldName.Contains("Rate") ||
                fieldName.Contains("Cost") || fieldName.Contains("Total"))
                return DiscrepancySeverity.Critical;

            // Name/identifier mismatches are critical
            if (fieldName == "Name" || fieldName.EndsWith("Ref.FullName"))
                return DiscrepancySeverity.Critical;

            // Missing vs truncated is a warning
            if (sourceVal.Length > 0 && targetVal.Length > 0 && sourceVal.StartsWith(targetVal))
                return DiscrepancySeverity.Info; // Likely truncation

            return DiscrepancySeverity.Warning;
        }

        private static string DetermineReason(string fieldName, string sourceVal, string targetVal)
        {
            if (string.IsNullOrEmpty(targetVal))
                return "Field value missing in target";
            if (string.IsNullOrEmpty(sourceVal))
                return "Field has unexpected value in target";
            if (sourceVal.StartsWith(targetVal) || targetVal.StartsWith(sourceVal))
                return "Value appears truncated";
            return "Values do not match";
        }

        private static int CountFields(JObject obj)
        {
            int count = 0;
            foreach (var prop in obj.Properties())
            {
                if (prop.Value is JObject nested)
                    count += CountFields(nested);
                else
                    count++;
            }
            return count;
        }

        private string BuildSummary(ValidationReport report)
        {
            var parts = new List<string>();
            var countMatches = report.EntityCountComparisons.Count(c => c.Matches);
            var countTotal = report.EntityCountComparisons.Count;
            parts.Add($"Entity counts: {countMatches}/{countTotal} match");
            parts.Add($"Field discrepancies: {report.TotalDiscrepancies} " +
                $"({report.CriticalDiscrepancies} critical, {report.WarningDiscrepancies} warnings)");
            if (report.FinancialReconciliation != null)
                parts.Add($"Financial reconciliation: {(report.FinancialReconciliation.IsReconciled ? "PASSED" : "FAILED")}");
            parts.Add($"Overall: {(report.IsValid ? "VALID" : "DISCREPANCIES FOUND")}");
            return string.Join(" | ", parts);
        }

        /// <summary>
        /// Saves the validation report to a JSON file.
        /// </summary>
        private void SaveReport(ValidationReport report)
        {
            Directory.CreateDirectory(_outputDirectory);
            var filePath = Path.Combine(_outputDirectory, $"ValidationReport_{DateTime.Now:yyyyMMdd_HHmmss}.json");

            var json = JsonConvert.SerializeObject(report, Formatting.Indented, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                Converters = { new Newtonsoft.Json.Converters.StringEnumConverter() }
            });

            File.WriteAllText(filePath, json);
            Log.Information("Validation report saved to: {FilePath}", filePath);
        }
    }
}
