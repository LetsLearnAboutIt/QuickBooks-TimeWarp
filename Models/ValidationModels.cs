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
}
