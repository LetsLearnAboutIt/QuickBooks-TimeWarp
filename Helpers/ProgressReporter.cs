using Serilog;

namespace QB_TimeWarp.Helpers
{
    /// <summary>
    /// Provides console progress indicators for long-running operations.
    /// Uses Spectre.Console for rich terminal output when available,
    /// falls back to simple console output otherwise.
    /// </summary>
    public class ProgressReporter
    {
        private readonly string _operationName;
        private int _totalItems;
        private int _processedItems;
        private int _successCount;
        private int _failureCount;
        private readonly DateTime _startTime;

        public ProgressReporter(string operationName, int totalItems = 0)
        {
            _operationName = operationName;
            _totalItems = totalItems;
            _startTime = DateTime.Now;
        }

        public void SetTotal(int total) => _totalItems = total;

        public void ReportProgress(string itemName, bool success = true)
        {
            _processedItems++;
            if (success) _successCount++;
            else _failureCount++;

            // Report every 10 items or at milestones
            if (_processedItems % 10 == 0 || _processedItems == _totalItems || _processedItems == 1)
            {
                var elapsed = DateTime.Now - _startTime;
                var pct = _totalItems > 0 ? (_processedItems * 100.0 / _totalItems) : 0;
                var eta = _processedItems > 0 && _totalItems > 0
                    ? TimeSpan.FromSeconds(elapsed.TotalSeconds / _processedItems * (_totalItems - _processedItems))
                    : TimeSpan.Zero;

                Console.Write($"\r  [{_operationName}] {_processedItems}/{_totalItems} ({pct:F0}%) " +
                    $"| ✓{_successCount} ✗{_failureCount} " +
                    $"| Elapsed: {elapsed:mm\\:ss} ETA: {eta:mm\\:ss}   ");
            }
        }

        public void Complete()
        {
            var elapsed = DateTime.Now - _startTime;
            Console.WriteLine();
            Log.Information("{Operation} complete: {Success} succeeded, {Failed} failed in {Elapsed:F1}s",
                _operationName, _successCount, _failureCount, elapsed.TotalSeconds);
        }
    }

    /// <summary>
    /// Displays a step-by-step execution plan in the console.
    /// </summary>
    public static class ConsoleBanner
    {
        public static void ShowHeader()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(@"
  ╔═══════════════════════════════════════════════════════════╗
  ║                                                           ║
  ║           QB-TimeWarp  v1.0.0                             ║
  ║           QuickBooks 2023 → 2021 Data Migration           ║
  ║                                                           ║
  ╚═══════════════════════════════════════════════════════════╝
");
            Console.ResetColor();
        }

        public static void ShowStep(int stepNumber, int totalSteps, string description)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n  ┌─ Step {stepNumber}/{totalSteps}: {description}");
            Console.WriteLine("  └────────────────────────────────────────");
            Console.ResetColor();
        }

        public static void ShowSuccess(string message)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  ✓ {message}");
            Console.ResetColor();
        }

        public static void ShowWarning(string message)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  ⚠ {message}");
            Console.ResetColor();
        }

        public static void ShowError(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  ✗ {message}");
            Console.ResetColor();
        }

        public static void ShowSummary(string title, Dictionary<string, string> items)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"\n  ┌─ {title}");
            Console.ResetColor();

            foreach (var (key, value) in items)
            {
                Console.WriteLine($"  │  {key,-30} : {value}");
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  └────────────────────────────────────────");
            Console.ResetColor();
        }
    }
}
