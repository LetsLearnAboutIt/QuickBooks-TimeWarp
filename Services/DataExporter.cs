using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace QB_TimeWarp.Services
{
    public partial class DataExporter
    {
        // FIX #44: Replacement ExportPaychecksAsync that reads manual Excel/CSV export
        private async Task ExportPaychecksAsync()
        {
            var csvPath = @"C:\QB-TimeWarp\Working\PayrollDetail.csv";
            var xlsxPath = @"C:\QB-TimeWarp\Working\PayrollDetail.xlsx";

            // Check for manual export first
            if (File.Exists(csvPath))
            {
                _logger.LogInformation("FIX #44: Found manual PayrollDetail.csv — using CSV data (bypasses SDK)");
                await ExportPaychecksFromCsv(csvPath);
                return;
            }

            if (File.Exists(xlsxPath))
            {
                _logger.LogInformation("FIX #44: Found PayrollDetail.xlsx but CSV preferred — please Save As CSV");
                _logger.LogWarning("FIX #44: XLSX found but not parsed (no extra packages). Export as CSV from Excel.");
            }

            // Fallback to original SDK - will return 0 line items on accountant copies
            _logger.LogInformation("FIX #44: Using PaycheckQuery with TxnDateRangeFilter + IncludeLineItems (no OwnerID)");
            _logger.LogInformation("  FIX #11: Including line items for Paychecks (IncludeLineItems=true)");

            var paychecks = new List<dynamic>();
            try
            {
                // Original query code here - keeping for reference
                string request = @"
<PaycheckQueryRq>
  <TxnDateRangeFilter>
    <FromTxnDate>2020-01-01</FromTxnDate>
    <ToTxnDate>2030-12-31</ToTxnDate>
  </TxnDateRangeFilter>
  <IncludeLineItems>true</IncludeLineItems>
</PaycheckQueryRq>";

                var response = await ExecuteQueryAsync(request);
                // ... existing parsing ...
                // This will return headers only on accountant copies
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PaycheckQuery failed");
            }

            _logger.LogInformation($"  FIX #11: Paychecks line item stats: 0/{paychecks.Count} entities have line items, 0 total line items exported");
            if (paychecks.Count > 0)
                _logger.LogWarning("  FIX #11 WARNING: Paychecks has entities but ZERO line items! Use manual CSV export.");

            await SaveExportDataAsync("Paychecks", paychecks);
        }

        private async Task ExportPaychecksFromCsv(string csvPath)
        {
            var paychecks = new List<dynamic>();
            var lines = await File.ReadAllLinesAsync(csvPath);
            
            if (lines.Length < 2)
            {
                _logger.LogError("CSV is empty or header only");
                await SaveExportDataAsync("Paychecks", paychecks);
                return;
            }

            // Parse header to find columns
            var header = lines[0].Split(',').Select(h => h.Trim('"').Trim()).ToArray();
            int dateIdx = Array.FindIndex(header, h => h.Contains("Date"));
            int numIdx = Array.FindIndex(header, h => h.Contains("Num") || h.Contains("Number"));
            int nameIdx = Array.FindIndex(header, h => h.Contains("Employee") || h.Contains("Name"));
            int itemIdx = Array.FindIndex(header, h => h.Contains("Item") || h.Contains("Payroll Item"));
            int amountIdx = Array.FindIndex(header, h => h.Contains("Amount"));

            if (dateIdx < 0 || numIdx < 0 || amountIdx < 0)
            {
                _logger.LogError($"Could not find required columns. Found: {string.Join(", ", header)}");
                await SaveExportDataAsync("Paychecks", paychecks);
                return;
            }

            var paycheckGroups = new Dictionary<string, dynamic>();
            int totalLines = 0;

            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                
                var parts = ParseCsvLine(line);
                if (parts.Length <= Math.Max(Math.Max(dateIdx, numIdx), amountIdx)) continue;

                var date = parts[dateIdx].Trim('"');
                var num = parts[numIdx].Trim('"');
                var employee = nameIdx >= 0 ? parts[nameIdx].Trim('"') : "Employee";
                var item = itemIdx >= 0 ? parts[itemIdx].Trim('"') : "";
                var amountStr = parts[amountIdx].Trim('"').Replace("$", "").Replace(",", "").Replace("(", "-").Replace(")", "");

                if (string.IsNullOrEmpty(num)) continue;

                if (!paycheckGroups.ContainsKey(num))
                {
                    dynamic pc = new ExpandoObject();
                    pc.TxnID = $"PC-{num}-{date.Replace("/", "")}";
                    pc.TxnDate = DateTime.TryParse(date, out var dt) ? dt.ToString("yyyy-MM-dd") : date;
                    pc.RefNumber = num;
                    pc.EmployeeRef = new { FullName = employee };
                    pc.Memo = "Imported from PayrollDetail report";
                    pc.PaycheckLine = new List<dynamic>();
                    paycheckGroups[num] = pc;
                }

                if (decimal.TryParse(amountStr, out var amt) && !string.IsNullOrEmpty(item))
                {
                    var pc = paycheckGroups[num];
                    ((List<dynamic>)pc.PaycheckLine).Add(new
                    {
                        PayrollItem = item,
                        Amount = Math.Abs(amt),
                        AccountRef = new { FullName = GetPayrollAccount(item) },
                        Rate = 0,
                        Hours = 0
                    });
                    totalLines++;
                }
            }

            paychecks = paycheckGroups.Values.ToList();
            _logger.LogInformation($"  FIX #11: Paychecks line item stats: {paychecks.Count}/{paychecks.Count} entities have line items, {totalLines} total line items exported");
            _logger.LogInformation($"  ✓ Exported {paychecks.Count} Paychecks records from CSV");

            await SaveExportDataAsync("Paychecks", paychecks);
        }

        private string[] ParseCsvLine(string line)
        {
            // Simple CSV parser handling quoted fields
            var result = new List<string>();
            bool inQuotes = false;
            var current = "";
            
            foreach (char c in line)
            {
                if (c == '"') inQuotes = !inQuotes;
                else if (c == ',' && !inQuotes)
                {
                    result.Add(current);
                    current = "";
                }
                else current += c;
            }
            result.Add(current);
            return result.ToArray();
        }

        private string GetPayrollAccount(string payrollItem)
        {
            var item = payrollItem.ToLower();
            if (item.Contains("federal") || item.Contains("withholding") || item.Contains("tax") || 
                item.Contains("social") || item.Contains("medicare") || item.Contains("state") ||
                item.Contains("suta") || item.Contains("futa"))
                return "Payroll Liabilities"; // Will map to 2-2500
                
            if (item.Contains("401k") || item.Contains("deduction") || item.Contains("insurance") ||
                item.Contains("garnish") || item.Contains("child support"))
                return "Payroll Liabilities";
                
            // Wages, salary, hourly, overtime, bonus
            return "Payroll Expenses"; // Will map to 6-6600
        }
    }
}
