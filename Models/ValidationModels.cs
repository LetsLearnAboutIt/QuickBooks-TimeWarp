using Newtonsoft.Json.Linq;

namespace QB_TimeWarp.Models
{
    /// <summary>
    /// Comprehensive validation report comparing source and target data.
    /// </summary>
    public class ValidationReport
    {
        public DateTime ValidatedAt { get; set; } = DateTime.UtcNow;
        public bool IsValid { get; set; }
        public string Summary { get; set; } = string.Empty;

        // Entity count verification
        public List<EntityCountComparison> EntityCountComparisons { get; set; } = new();

        // Field-by-field comparison results
        public List<FieldComparisonResult> FieldDiscrepancies { get; set; } = new();

        // Financial reconciliation
        public FinancialReconciliation FinancialReconciliation { get; set; } = new();

        // Journal integrity check results
        public JournalIntegrityReport? JournalIntegrity { get; set; }

        // Overall stats
        public int TotalEntitiesCompared { get; set; }
        public int TotalFieldsCompared { get; set; }
        public int TotalDiscrepancies { get; set; }
        public int CriticalDiscrepancies { get; set; }
        public int WarningDiscrepancies { get; set; }
    }

    /// <summary>
    /// Compares entity counts between source and target.
    /// </summary>
    public class EntityCountComparison
    {
        public string EntityType { get; set; } = string.Empty;
        public int SourceCount { get; set; }
        public int TargetCount { get; set; }
        public int Difference => TargetCount - SourceCount;
        public bool Matches => SourceCount == TargetCount;
        public string? Notes { get; set; }
    }

    /// <summary>
    /// Result of comparing a single field between source and target.
    /// </summary>
    public class FieldComparisonResult
    {
        public string EntityType { get; set; } = string.Empty;
        public string EntityIdentifier { get; set; } = string.Empty;
        public string FieldName { get; set; } = string.Empty;
        public string? SourceValue { get; set; }
        public string? TargetValue { get; set; }
        public DiscrepancySeverity Severity { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    public enum DiscrepancySeverity
    {
        Info,
        Warning,
        Critical
    }

    /// <summary>
    /// Financial totals reconciliation between source and target.
    /// </summary>
    public class FinancialReconciliation
    {
        public bool IsReconciled { get; set; }
        public List<AccountBalanceComparison> AccountBalances { get; set; } = new();
        public BalanceSummary ARSummary { get; set; } = new();
        public BalanceSummary APSummary { get; set; } = new();
        public List<TransactionTotalComparison> TransactionTotals { get; set; } = new();
    }

    /// <summary>
    /// Compares a single account balance between source and target.
    /// </summary>
    public class AccountBalanceComparison
    {
        public string AccountName { get; set; } = string.Empty;
        public string AccountType { get; set; } = string.Empty;
        public decimal SourceBalance { get; set; }
        public decimal TargetBalance { get; set; }
        public decimal Difference => TargetBalance - SourceBalance;
        public bool IsWithinTolerance(decimal tolerance) => Math.Abs(Difference) <= tolerance;
    }

    /// <summary>
    /// Summary balance comparison (AR, AP).
    /// </summary>
    public class BalanceSummary
    {
        public string Category { get; set; } = string.Empty;
        public decimal SourceTotal { get; set; }
        public decimal TargetTotal { get; set; }
        public decimal Difference => TargetTotal - SourceTotal;
        public bool IsWithinTolerance(decimal tolerance) => Math.Abs(Difference) <= tolerance;
    }

    /// <summary>
    /// Compares transaction totals by type between source and target.
    /// </summary>
    public class TransactionTotalComparison
    {
        public string TransactionType { get; set; } = string.Empty;
        public int SourceCount { get; set; }
        public int TargetCount { get; set; }
        public decimal SourceTotalAmount { get; set; }
        public decimal TargetTotalAmount { get; set; }
        public decimal AmountDifference => TargetTotalAmount - SourceTotalAmount;
        public int CountDifference => TargetCount - SourceCount;
        public bool IsWithinTolerance(decimal tolerance) => Math.Abs(AmountDifference) <= tolerance;
    }

    // ═══════════════════════════════════════════════════════════════════
    // Journal Integrity Check Models
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Comprehensive journal integrity validation report.
    /// Ensures all journal entries have balanced debits and credits,
    /// and validates financial integrity across all transaction types.
    /// </summary>
    public class JournalIntegrityReport
    {
        public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
        public bool IsBalanced { get; set; }
        public string Summary { get; set; } = string.Empty;

        // Journal entry debit/credit validation
        public int TotalJournalEntriesChecked { get; set; }
        public int BalancedJournalEntries { get; set; }
        public int UnbalancedJournalEntries { get; set; }
        public List<JournalMismatchDetail> JournalMismatches { get; set; } = new();

        // Invoice line item validation
        public int TotalInvoicesChecked { get; set; }
        public int InvoicesWithLineItemMismatch { get; set; }
        public List<JournalMismatchDetail> InvoiceMismatches { get; set; } = new();

        // Bill amount validation
        public int TotalBillsChecked { get; set; }
        public int BillsWithAmountMismatch { get; set; }
        public List<JournalMismatchDetail> BillMismatches { get; set; } = new();

        // Payment application validation
        public int TotalPaymentsChecked { get; set; }
        public int UnbalancedPayments { get; set; }
        public List<JournalMismatchDetail> PaymentMismatches { get; set; } = new();

        // General ledger transaction validation
        public int TotalGLTransactionsChecked { get; set; }
        public int GLTransactionsWithIssues { get; set; }
        public List<JournalMismatchDetail> GLMismatches { get; set; } = new();
    }

    /// <summary>
    /// Detailed record of a journal entry mismatch or financial discrepancy.
    /// </summary>
    public class JournalMismatchDetail
    {
        public string TransactionType { get; set; } = string.Empty;
        public string ReferenceNumber { get; set; } = string.Empty;
        public string TxnID { get; set; } = string.Empty;
        public string TxnDate { get; set; } = string.Empty;
        public decimal ExpectedDebitTotal { get; set; }
        public decimal ActualDebitTotal { get; set; }
        public decimal ExpectedCreditTotal { get; set; }
        public decimal ActualCreditTotal { get; set; }
        public decimal Variance => Math.Abs(ActualDebitTotal - ActualCreditTotal);
        public JournalMismatchSeverity Severity { get; set; }
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>
    /// Severity levels for journal mismatches.
    /// </summary>
    public enum JournalMismatchSeverity
    {
        /// <summary>Variance within tolerance (rounding).</summary>
        Info,
        /// <summary>Small variance that may indicate data issue.</summary>
        Warning,
        /// <summary>Significant imbalance requiring investigation.</summary>
        Critical
    }
}
