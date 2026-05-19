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

            // 4. Journal integrity check (pre-import and post-import)
            if (_validationConfig.EnableJournalValidation)
            {
                Log.Information("Running journal integrity check...");
                report.JournalIntegrity = ValidateJournalIntegrity(sourceData, importedData);
            }

            // Calculate totals
            report.TotalDiscrepancies = report.FieldDiscrepancies.Count;
            report.CriticalDiscrepancies = report.FieldDiscrepancies
                .Count(d => d.Severity == DiscrepancySeverity.Critical);
            report.WarningDiscrepancies = report.FieldDiscrepancies
                .Count(d => d.Severity == DiscrepancySeverity.Warning);

            report.IsValid = report.CriticalDiscrepancies == 0
                && report.EntityCountComparisons.All(c => c.Matches)
                && (report.FinancialReconciliation?.IsReconciled ?? true)
                && (report.JournalIntegrity?.IsBalanced ?? true);

            report.Summary = BuildSummary(report);

            // Save report
            SaveReport(report);

            Log.Information("═══════════════════════════════════════════════════════");
            Log.Information("  VALIDATION {Status}: {Critical} critical, {Warning} warnings",
                report.IsValid ? "PASSED" : "FAILED",
                report.CriticalDiscrepancies, report.WarningDiscrepancies);
            if (report.JournalIntegrity != null)
            {
                Log.Information("  Journal Integrity: {Status} ({Checked} entries checked, {Mismatches} mismatches)",
                    report.JournalIntegrity.IsBalanced ? "BALANCED" : "IMBALANCED",
                    report.JournalIntegrity.TotalJournalEntriesChecked,
                    report.JournalIntegrity.UnbalancedJournalEntries);
            }
            Log.Information("═══════════════════════════════════════════════════════");

            return report;
        }

        // ═══════════════════════════════════════════════════════════════════
        // JOURNAL INTEGRITY VALIDATION
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Performs comprehensive journal integrity validation on both source and imported data.
        /// Validates: journal entries (debits=credits), invoice line items, bill amounts,
        /// payment applications, and general ledger transaction balance.
        /// </summary>
        public JournalIntegrityReport ValidateJournalIntegrity(
            Dictionary<string, ExportedEntitySet> sourceData,
            Dictionary<string, ExportedEntitySet>? importedData = null)
        {
            Log.Information("────────────────────────────────────────────");
            Log.Information("  JOURNAL INTEGRITY CHECK");
            Log.Information("────────────────────────────────────────────");

            var report = new JournalIntegrityReport { CheckedAt = DateTime.UtcNow };

            // Check source data first (pre-import validation)
            Log.Information("  Checking source data journal integrity...");
            ValidateJournalEntriesBalance(sourceData, report, "Source");
            ValidateInvoiceLineItems(sourceData, report, "Source");
            ValidateBillAmounts(sourceData, report, "Source");
            ValidatePaymentApplications(sourceData, report, "Source");

            // Check imported data if available (post-import validation)
            if (importedData != null && importedData.Any())
            {
                Log.Information("  Checking imported data journal integrity...");
                ValidateJournalEntriesBalance(importedData, report, "Target");
                ValidateInvoiceLineItems(importedData, report, "Target");
                ValidateBillAmounts(importedData, report, "Target");
                ValidatePaymentApplications(importedData, report, "Target");
            }

            // Determine overall balance status
            report.IsBalanced = report.UnbalancedJournalEntries == 0
                && report.InvoicesWithLineItemMismatch == 0
                && report.BillsWithAmountMismatch == 0
                && report.UnbalancedPayments == 0;

            // Build summary
            var parts = new List<string>();
            parts.Add($"Journal entries: {report.BalancedJournalEntries}/{report.TotalJournalEntriesChecked} balanced");
            if (report.TotalInvoicesChecked > 0)
                parts.Add($"Invoices: {report.TotalInvoicesChecked - report.InvoicesWithLineItemMismatch}/{report.TotalInvoicesChecked} valid");
            if (report.TotalBillsChecked > 0)
                parts.Add($"Bills: {report.TotalBillsChecked - report.BillsWithAmountMismatch}/{report.TotalBillsChecked} valid");
            if (report.TotalPaymentsChecked > 0)
                parts.Add($"Payments: {report.TotalPaymentsChecked - report.UnbalancedPayments}/{report.TotalPaymentsChecked} balanced");
            report.Summary = string.Join(" | ", parts);

            if (report.IsBalanced)
            {
                Log.Information("  ✓ Journal integrity check PASSED: {Summary}", report.Summary);
            }
            else
            {
                Log.Warning("  ✗ Journal integrity check FAILED: {Summary}", report.Summary);
                foreach (var mismatch in report.JournalMismatches.Where(m => m.Severity == JournalMismatchSeverity.Critical).Take(10))
                {
                    Log.Warning("    CRITICAL: {Type} Ref#{Ref} - Debits: {Debits:C}, Credits: {Credits:C}, Variance: {Variance:C}",
                        mismatch.TransactionType, mismatch.ReferenceNumber,
                        mismatch.ActualDebitTotal, mismatch.ActualCreditTotal, mismatch.Variance);
                }
            }

            return report;
        }

        /// <summary>
        /// Pre-import journal validation: validates source data integrity before importing to QB 2021.
        /// Call this before running the import to catch issues early.
        /// </summary>
        public JournalIntegrityReport ValidatePreImport(Dictionary<string, ExportedEntitySet> sourceData)
        {
            Log.Information("════════════════════════════════════════════════════════");
            Log.Information("  PRE-IMPORT JOURNAL VALIDATION");
            Log.Information("════════════════════════════════════════════════════════");

            return ValidateJournalIntegrity(sourceData, null);
        }

        /// <summary>
        /// Validates that all journal entries have balanced debits and credits.
        /// For every JE, total debit line amounts must equal total credit line amounts.
        /// </summary>
        private void ValidateJournalEntriesBalance(
            Dictionary<string, ExportedEntitySet> data,
            JournalIntegrityReport report, string dataSource)
        {
            if (!data.ContainsKey("JournalEntries")) return;

            var journalEntries = data["JournalEntries"].Entities;
            Log.Information("    Validating {Count} journal entries ({Source})...", journalEntries.Count, dataSource);

            foreach (var je in journalEntries)
            {
                report.TotalJournalEntriesChecked++;

                decimal totalDebits = 0;
                decimal totalCredits = 0;

                foreach (var lineItem in je.LineItems)
                {
                    var lineType = lineItem["_lineType"]?.ToString() ?? string.Empty;
                    var amount = GetDecimalField(lineItem, "Amount");

                    if (lineType.Contains("Debit", StringComparison.OrdinalIgnoreCase))
                    {
                        totalDebits += amount;
                    }
                    else if (lineType.Contains("Credit", StringComparison.OrdinalIgnoreCase))
                    {
                        totalCredits += amount;
                    }
                }

                var variance = Math.Abs(totalDebits - totalCredits);
                if (variance <= _validationConfig.ToleranceAmount)
                {
                    report.BalancedJournalEntries++;
                }
                else
                {
                    report.UnbalancedJournalEntries++;
                    var severity = variance > 1.00M ? JournalMismatchSeverity.Critical : JournalMismatchSeverity.Warning;

                    report.JournalMismatches.Add(new JournalMismatchDetail
                    {
                        TransactionType = "JournalEntry",
                        ReferenceNumber = je.Fields["RefNumber"]?.ToString() ?? "N/A",
                        TxnID = je.TxnID,
                        TxnDate = je.Fields["TxnDate"]?.ToString() ?? "N/A",
                        ExpectedDebitTotal = totalCredits, // Expected to match credits
                        ActualDebitTotal = totalDebits,
                        ExpectedCreditTotal = totalDebits, // Expected to match debits
                        ActualCreditTotal = totalCredits,
                        Severity = severity,
                        Description = $"[{dataSource}] Journal entry debits ({totalDebits:C}) do not equal credits ({totalCredits:C}). Variance: {variance:C}"
                    });

                    Log.Warning("      Unbalanced JE Ref#{Ref}: Debits={Debits:F2}, Credits={Credits:F2}, Variance={Variance:F2}",
                        je.Fields["RefNumber"]?.ToString() ?? "N/A", totalDebits, totalCredits, variance);
                }
            }
        }

        /// <summary>
        /// Validates that invoice line items sum to the invoice subtotal/total.
        /// </summary>
        private void ValidateInvoiceLineItems(
            Dictionary<string, ExportedEntitySet> data,
            JournalIntegrityReport report, string dataSource)
        {
            if (!data.ContainsKey("Invoices")) return;

            var invoices = data["Invoices"].Entities;

            foreach (var invoice in invoices)
            {
                report.TotalInvoicesChecked++;

                // Get the stated subtotal from the invoice header
                var statedSubtotal = GetDecimalField(invoice.Fields, "Subtotal");
                if (statedSubtotal == 0)
                    statedSubtotal = GetDecimalField(invoice.Fields, "TotalAmount");

                // Sum line item amounts
                decimal lineItemTotal = 0;
                foreach (var line in invoice.LineItems)
                {
                    lineItemTotal += GetDecimalField(line, "Amount");
                }

                // If no line items or no stated total, skip comparison
                if (lineItemTotal == 0 && statedSubtotal == 0) continue;
                if (!invoice.LineItems.Any()) continue;

                var variance = Math.Abs(statedSubtotal - lineItemTotal);
                if (variance > _validationConfig.ToleranceAmount)
                {
                    report.InvoicesWithLineItemMismatch++;
                    var severity = variance > 1.00M ? JournalMismatchSeverity.Critical : JournalMismatchSeverity.Warning;

                    report.InvoiceMismatches.Add(new JournalMismatchDetail
                    {
                        TransactionType = "Invoice",
                        ReferenceNumber = invoice.Fields["RefNumber"]?.ToString() ?? "N/A",
                        TxnID = invoice.TxnID,
                        TxnDate = invoice.Fields["TxnDate"]?.ToString() ?? "N/A",
                        ExpectedDebitTotal = statedSubtotal,
                        ActualDebitTotal = lineItemTotal,
                        ExpectedCreditTotal = statedSubtotal,
                        ActualCreditTotal = lineItemTotal,
                        Severity = severity,
                        Description = $"[{dataSource}] Invoice line items total ({lineItemTotal:C}) does not match stated subtotal ({statedSubtotal:C}). Variance: {variance:C}"
                    });
                }
            }
        }

        /// <summary>
        /// Validates that bill amounts match their line item totals.
        /// </summary>
        private void ValidateBillAmounts(
            Dictionary<string, ExportedEntitySet> data,
            JournalIntegrityReport report, string dataSource)
        {
            if (!data.ContainsKey("Bills")) return;

            var bills = data["Bills"].Entities;

            foreach (var bill in bills)
            {
                report.TotalBillsChecked++;

                var statedAmount = GetDecimalField(bill.Fields, "AmountDue");
                if (statedAmount == 0)
                    statedAmount = GetDecimalField(bill.Fields, "TotalAmount");

                // Sum line item amounts (expense lines + item lines)
                decimal lineItemTotal = 0;
                foreach (var line in bill.LineItems)
                {
                    lineItemTotal += GetDecimalField(line, "Amount");
                }

                if (lineItemTotal == 0 && statedAmount == 0) continue;
                if (!bill.LineItems.Any()) continue;

                var variance = Math.Abs(statedAmount - lineItemTotal);
                if (variance > _validationConfig.ToleranceAmount)
                {
                    report.BillsWithAmountMismatch++;
                    var severity = variance > 1.00M ? JournalMismatchSeverity.Critical : JournalMismatchSeverity.Warning;

                    report.BillMismatches.Add(new JournalMismatchDetail
                    {
                        TransactionType = "Bill",
                        ReferenceNumber = bill.Fields["RefNumber"]?.ToString() ?? "N/A",
                        TxnID = bill.TxnID,
                        TxnDate = bill.Fields["TxnDate"]?.ToString() ?? "N/A",
                        ExpectedDebitTotal = statedAmount,
                        ActualDebitTotal = lineItemTotal,
                        ExpectedCreditTotal = statedAmount,
                        ActualCreditTotal = lineItemTotal,
                        Severity = severity,
                        Description = $"[{dataSource}] Bill line items total ({lineItemTotal:C}) does not match stated amount ({statedAmount:C}). Variance: {variance:C}"
                    });
                }
            }
        }

        /// <summary>
        /// Validates that payment applications are balanced (payment amount matches applied amounts).
        /// </summary>
        private void ValidatePaymentApplications(
            Dictionary<string, ExportedEntitySet> data,
            JournalIntegrityReport report, string dataSource)
        {
            if (!data.ContainsKey("Payments")) return;

            var payments = data["Payments"].Entities;

            foreach (var payment in payments)
            {
                report.TotalPaymentsChecked++;

                var totalAmount = GetDecimalField(payment.Fields, "TotalAmount");
                var unusedAmount = GetDecimalField(payment.Fields, "UnusedPayment");

                // Sum applied amounts from line items
                decimal appliedTotal = 0;
                foreach (var line in payment.LineItems)
                {
                    appliedTotal += GetDecimalField(line, "Amount");
                    appliedTotal += GetDecimalField(line, "PaymentAmount");
                }

                // If no applied lines, the full amount should be accounted for
                if (!payment.LineItems.Any()) continue;

                var expectedApplied = totalAmount - unusedAmount;
                var variance = Math.Abs(expectedApplied - appliedTotal);

                if (variance > _validationConfig.ToleranceAmount)
                {
                    report.UnbalancedPayments++;
                    var severity = variance > 1.00M ? JournalMismatchSeverity.Critical : JournalMismatchSeverity.Warning;

                    report.PaymentMismatches.Add(new JournalMismatchDetail
                    {
                        TransactionType = "Payment",
                        ReferenceNumber = payment.Fields["RefNumber"]?.ToString() ?? "N/A",
                        TxnID = payment.TxnID,
                        TxnDate = payment.Fields["TxnDate"]?.ToString() ?? "N/A",
                        ExpectedDebitTotal = expectedApplied,
                        ActualDebitTotal = appliedTotal,
                        ExpectedCreditTotal = totalAmount,
                        ActualCreditTotal = appliedTotal + unusedAmount,
                        Severity = severity,
                        Description = $"[{dataSource}] Payment applied total ({appliedTotal:C}) does not match expected ({expectedApplied:C}). Total: {totalAmount:C}, Unused: {unusedAmount:C}"
                    });
                }
            }
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
            if (report.JournalIntegrity != null)
                parts.Add($"Journal integrity: {(report.JournalIntegrity.IsBalanced ? "BALANCED" : "IMBALANCED")} ({report.JournalIntegrity.Summary})");
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
