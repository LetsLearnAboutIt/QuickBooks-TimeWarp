using Newtonsoft.Json.Linq;
using QB_TimeWarp.Models;
using Serilog;

namespace QB_TimeWarp.Helpers
{
    /// <summary>
    /// Scans exported JSON data to build a dependency graph.
    /// Identifies all referenced items (sales tax, accounts, classes, terms, etc.)
    /// so the staged importer can ensure they exist before importing entities that reference them.
    /// </summary>
    public class DependencyAnalyzer
    {
        /// <summary>
        /// Result of a dependency analysis — grouped lists of required items by type.
        /// </summary>
        public class DependencyAnalysisResult
        {
            public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;

            /// <summary>Account FullNames referenced by entities/transactions.</summary>
            public HashSet<string> ReferencedAccounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>Sales tax code names referenced by customers/transactions.</summary>
            public HashSet<string> ReferencedSalesTaxCodes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>Sales tax item names referenced by customers/invoices.</summary>
            public HashSet<string> ReferencedSalesTaxItems { get; set; } = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>Payment terms names referenced by customers/vendors/invoices.</summary>
            public HashSet<string> ReferencedTerms { get; set; } = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>Payment method names referenced by customers/payments.</summary>
            public HashSet<string> ReferencedPaymentMethods { get; set; } = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>Class FullNames referenced by any entity or transaction.</summary>
            public HashSet<string> ReferencedClasses { get; set; } = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>Customer FullNames referenced by transactions.</summary>
            public HashSet<string> ReferencedCustomers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>Vendor FullNames referenced by transactions.</summary>
            public HashSet<string> ReferencedVendors { get; set; } = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>Customer type names referenced by customers.</summary>
            public HashSet<string> ReferencedCustomerTypes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>Vendor type names referenced by vendors.</summary>
            public HashSet<string> ReferencedVendorTypes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>Job type names referenced by customers (jobs).</summary>
            public HashSet<string> ReferencedJobTypes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>Ship method names referenced by invoices/sales receipts.</summary>
            public HashSet<string> ReferencedShipMethods { get; set; } = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>Price level names referenced by customers/items.</summary>
            public HashSet<string> ReferencedPriceLevels { get; set; } = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>Item FullNames referenced by transaction line items.</summary>
            public HashSet<string> ReferencedItems { get; set; } = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>Entity types that have data to import.</summary>
            public HashSet<string> EntityTypesPresent { get; set; } = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>Per-entity-type count of records found.</summary>
            public Dictionary<string, int> RecordCounts { get; set; } = new();

            /// <summary>
            /// Items that are missing (referenced but not present in export data).
            /// Keyed by type (e.g., "SalesTaxCodes", "Terms"), value is list of missing names.
            /// </summary>
            public Dictionary<string, List<string>> MissingItems { get; set; } = new();

            /// <summary>Summary of analysis for logging.</summary>
            public string GetSummary()
            {
                var lines = new List<string>
                {
                    $"Entity types present: {EntityTypesPresent.Count}",
                    $"Referenced accounts: {ReferencedAccounts.Count}",
                    $"Referenced sales tax codes: {ReferencedSalesTaxCodes.Count}",
                    $"Referenced sales tax items: {ReferencedSalesTaxItems.Count}",
                    $"Referenced terms: {ReferencedTerms.Count}",
                    $"Referenced payment methods: {ReferencedPaymentMethods.Count}",
                    $"Referenced classes: {ReferencedClasses.Count}",
                    $"Referenced customers: {ReferencedCustomers.Count}",
                    $"Referenced vendors: {ReferencedVendors.Count}",
                    $"Referenced customer types: {ReferencedCustomerTypes.Count}",
                    $"Referenced vendor types: {ReferencedVendorTypes.Count}",
                    $"Referenced job types: {ReferencedJobTypes.Count}",
                    $"Referenced ship methods: {ReferencedShipMethods.Count}",
                    $"Referenced price levels: {ReferencedPriceLevels.Count}",
                    $"Referenced items: {ReferencedItems.Count}",
                };

                if (MissingItems.Any())
                {
                    lines.Add("--- Missing Items ---");
                    foreach (var (type, names) in MissingItems)
                    {
                        lines.Add($"  {type}: {names.Count} missing ({string.Join(", ", names.Take(10))}{(names.Count > 10 ? "..." : "")})");
                    }
                }

                return string.Join(Environment.NewLine, lines);
            }
        }

        /// <summary>
        /// Reference field names to their dependency type mapping.
        /// </summary>
        private static readonly Dictionary<string, string> RefFieldToDependencyType = new(StringComparer.OrdinalIgnoreCase)
        {
            ["AccountRef"] = "Accounts",
            ["ARAccountRef"] = "Accounts",
            ["APAccountRef"] = "Accounts",
            ["IncomeAccountRef"] = "Accounts",
            ["COGSAccountRef"] = "Accounts",
            ["AssetAccountRef"] = "Accounts",
            ["ExpenseAccountRef"] = "Accounts",
            ["DepositToAccountRef"] = "Accounts",
            ["BankAccountRef"] = "Accounts",
            ["SalesTaxCodeRef"] = "SalesTaxCodes",
            ["ItemSalesTaxRef"] = "SalesTaxItems",
            ["TermsRef"] = "Terms",
            ["PaymentMethodRef"] = "PaymentMethods",
            ["PreferredPaymentMethodRef"] = "PaymentMethods",
            ["ClassRef"] = "Classes",
            ["CustomerRef"] = "Customers",
            ["VendorRef"] = "Vendors",
            ["EntityRef"] = "Entities",
            ["CustomerTypeRef"] = "CustomerTypes",
            ["VendorTypeRef"] = "VendorTypes",
            ["JobTypeRef"] = "JobTypes",
            ["ShipMethodRef"] = "ShipMethods",
            ["PriceLevelRef"] = "PriceLevels",
            ["ItemRef"] = "Items",
        };

        /// <summary>
        /// Analyzes all exported data to build a complete dependency graph.
        /// Scans every entity's fields and line items for references.
        /// </summary>
        public DependencyAnalysisResult Analyze(Dictionary<string, ExportedEntitySet> exportedData)
        {
            var result = new DependencyAnalysisResult();

            Log.Information("═══════════════════════════════════════════════════════");
            Log.Information("  DEPENDENCY ANALYSIS — Scanning exported data");
            Log.Information("═══════════════════════════════════════════════════════");

            // Record what entity types are present and their counts
            foreach (var (entityType, entitySet) in exportedData)
            {
                if (entitySet.TotalCount > 0)
                {
                    result.EntityTypesPresent.Add(entityType);
                    result.RecordCounts[entityType] = entitySet.TotalCount;
                }
            }

            // Scan each entity type for references
            foreach (var (entityType, entitySet) in exportedData)
            {
                if (entitySet.TotalCount == 0) continue;

                int refCount = 0;
                foreach (var entity in entitySet.Entities)
                {
                    // Scan top-level fields
                    refCount += ScanFieldsForReferences(entity.Fields, result);

                    // Scan line items
                    foreach (var lineItem in entity.LineItems)
                    {
                        refCount += ScanFieldsForReferences(lineItem, result);
                    }
                }

                if (refCount > 0)
                {
                    Log.Debug("  {EntityType}: found {Count} references", entityType, refCount);
                }
            }

            // Identify missing items — things referenced but not in exported data
            IdentifyMissingItems(result, exportedData);

            // Log summary
            Log.Information("────────────────────────────────────────────");
            Log.Information("  DEPENDENCY ANALYSIS RESULTS:");
            Log.Information("────────────────────────────────────────────");
            Log.Information("  Accounts referenced: {Count}", result.ReferencedAccounts.Count);
            Log.Information("  Sales tax codes referenced: {Count}", result.ReferencedSalesTaxCodes.Count);
            Log.Information("  Sales tax items referenced: {Count}", result.ReferencedSalesTaxItems.Count);
            Log.Information("  Terms referenced: {Count}", result.ReferencedTerms.Count);
            Log.Information("  Payment methods referenced: {Count}", result.ReferencedPaymentMethods.Count);
            Log.Information("  Classes referenced: {Count}", result.ReferencedClasses.Count);
            Log.Information("  Customer types referenced: {Count}", result.ReferencedCustomerTypes.Count);
            Log.Information("  Vendor types referenced: {Count}", result.ReferencedVendorTypes.Count);
            Log.Information("  Job types referenced: {Count}", result.ReferencedJobTypes.Count);
            Log.Information("  Ship methods referenced: {Count}", result.ReferencedShipMethods.Count);
            Log.Information("  Price levels referenced: {Count}", result.ReferencedPriceLevels.Count);
            Log.Information("  Customers referenced by txns: {Count}", result.ReferencedCustomers.Count);
            Log.Information("  Vendors referenced by txns: {Count}", result.ReferencedVendors.Count);
            Log.Information("  Items referenced by txns: {Count}", result.ReferencedItems.Count);

            if (result.MissingItems.Any())
            {
                Log.Warning("────────────────────────────────────────────");
                Log.Warning("  MISSING DEPENDENCIES DETECTED:");
                foreach (var (type, names) in result.MissingItems)
                {
                    Log.Warning("    {Type}: {Count} missing — {Names}",
                        type, names.Count,
                        string.Join(", ", names.Take(20)));
                }
            }
            else
            {
                Log.Information("  ✓ No missing dependencies detected in exported data.");
            }

            Log.Information("═══════════════════════════════════════════════════════");

            return result;
        }

        /// <summary>
        /// Scans a JObject's properties for reference fields and extracts the referenced names.
        /// </summary>
        private int ScanFieldsForReferences(JObject fields, DependencyAnalysisResult result)
        {
            int count = 0;

            foreach (var prop in fields.Properties())
            {
                // Check if this is a known reference field
                if (RefFieldToDependencyType.TryGetValue(prop.Name, out var depType))
                {
                    var refName = ExtractRefName(prop.Value);
                    if (!string.IsNullOrEmpty(refName))
                    {
                        AddReferencedItem(result, depType, refName);
                        count++;
                    }
                }

                // Also scan nested objects for references (e.g., line item refs)
                if (prop.Value is JObject nested && !prop.Name.EndsWith("Ref"))
                {
                    count += ScanFieldsForReferences(nested, result);
                }
            }

            return count;
        }

        /// <summary>
        /// Extracts the FullName (or Name/ListID) from a reference field value.
        /// Reference fields can be: { "FullName": "X" }, { "ListID": "Y" }, or a plain string.
        /// </summary>
        private static string? ExtractRefName(JToken value)
        {
            if (value is JObject refObj)
            {
                return refObj["FullName"]?.ToString()
                    ?? refObj["Name"]?.ToString()
                    ?? refObj["ListID"]?.ToString();
            }

            if (value.Type == JTokenType.String)
            {
                var str = value.ToString();
                return string.IsNullOrWhiteSpace(str) ? null : str;
            }

            return null;
        }

        /// <summary>
        /// Adds a referenced item name to the appropriate set in the result.
        /// </summary>
        private static void AddReferencedItem(DependencyAnalysisResult result, string depType, string name)
        {
            switch (depType)
            {
                case "Accounts": result.ReferencedAccounts.Add(name); break;
                case "SalesTaxCodes": result.ReferencedSalesTaxCodes.Add(name); break;
                case "SalesTaxItems": result.ReferencedSalesTaxItems.Add(name); break;
                case "Terms": result.ReferencedTerms.Add(name); break;
                case "PaymentMethods": result.ReferencedPaymentMethods.Add(name); break;
                case "Classes": result.ReferencedClasses.Add(name); break;
                case "Customers": result.ReferencedCustomers.Add(name); break;
                case "Vendors": result.ReferencedVendors.Add(name); break;
                case "CustomerTypes": result.ReferencedCustomerTypes.Add(name); break;
                case "VendorTypes": result.ReferencedVendorTypes.Add(name); break;
                case "JobTypes": result.ReferencedJobTypes.Add(name); break;
                case "ShipMethods": result.ReferencedShipMethods.Add(name); break;
                case "PriceLevels": result.ReferencedPriceLevels.Add(name); break;
                case "Items": result.ReferencedItems.Add(name); break;
                case "Entities":
                    // EntityRef could be customer or vendor — add to both for safety
                    result.ReferencedCustomers.Add(name);
                    result.ReferencedVendors.Add(name);
                    break;
            }
        }

        /// <summary>
        /// Compares referenced items against what's available in the exported data
        /// and identifies items that are referenced but not present.
        /// </summary>
        private void IdentifyMissingItems(DependencyAnalysisResult result, Dictionary<string, ExportedEntitySet> exportedData)
        {
            // Build sets of available items from exported data
            var availableNames = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var (entityType, entitySet) in exportedData)
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entity in entitySet.Entities)
                {
                    if (!string.IsNullOrEmpty(entity.FullName))
                        names.Add(entity.FullName);
                    if (!string.IsNullOrEmpty(entity.Name))
                        names.Add(entity.Name);
                }
                availableNames[entityType] = names;
            }

            // Check each reference type
            CheckMissing(result, "SalesTaxCodes", result.ReferencedSalesTaxCodes, availableNames);
            CheckMissing(result, "Terms", result.ReferencedTerms, availableNames);
            CheckMissing(result, "PaymentMethods", result.ReferencedPaymentMethods, availableNames);
            CheckMissing(result, "Classes", result.ReferencedClasses, availableNames);
            CheckMissing(result, "CustomerTypes", result.ReferencedCustomerTypes, availableNames);
            CheckMissing(result, "VendorTypes", result.ReferencedVendorTypes, availableNames);
            CheckMissing(result, "JobTypes", result.ReferencedJobTypes, availableNames);
            CheckMissing(result, "ShipMethods", result.ReferencedShipMethods, availableNames);
            CheckMissing(result, "PriceLevels", result.ReferencedPriceLevels, availableNames);
            // Note: Accounts, Customers, Vendors, Items are usually all in exported data.
            // We check them too but they are Stage 1-3 items.
            CheckMissing(result, "Accounts", result.ReferencedAccounts, availableNames);
            CheckMissing(result, "Customers", result.ReferencedCustomers, availableNames);
            CheckMissing(result, "Vendors", result.ReferencedVendors, availableNames);
        }

        /// <summary>
        /// Checks if referenced items exist in the available exported data.
        /// </summary>
        private static void CheckMissing(
            DependencyAnalysisResult result,
            string entityType,
            HashSet<string> referenced,
            Dictionary<string, HashSet<string>> availableNames)
        {
            if (!referenced.Any()) return;

            // For Items, check both "Items" key and item subtypes
            var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (availableNames.TryGetValue(entityType, out var names))
            {
                foreach (var n in names) available.Add(n);
            }

            // Also check "SalesTaxItems" separately if checking SalesTaxCodes
            if (entityType == "SalesTaxItems" && availableNames.TryGetValue("SalesTaxCodes", out var stcNames))
            {
                foreach (var n in stcNames) available.Add(n);
            }

            var missing = referenced.Where(r => !available.Contains(r)).OrderBy(r => r).ToList();
            if (missing.Any())
            {
                result.MissingItems[entityType] = missing;
            }
        }

        /// <summary>
        /// Loads and analyzes exported JSON files from the export directory.
        /// </summary>
        public DependencyAnalysisResult AnalyzeFromFiles(string exportDirectory)
        {
            Log.Information("  Loading exported data from: {Dir}", exportDirectory);

            var exportedData = new Dictionary<string, ExportedEntitySet>();

            if (!Directory.Exists(exportDirectory))
            {
                Log.Warning("  Export directory not found: {Dir}", exportDirectory);
                return new DependencyAnalysisResult();
            }

            foreach (var file in Directory.GetFiles(exportDirectory, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var entitySet = Newtonsoft.Json.JsonConvert.DeserializeObject<ExportedEntitySet>(json);
                    if (entitySet != null && !string.IsNullOrEmpty(entitySet.EntityType))
                    {
                        exportedData[entitySet.EntityType] = entitySet;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning("  Could not parse {File}: {Message}", Path.GetFileName(file), ex.Message);
                }
            }

            Log.Information("  Loaded {Count} entity types from exported files.", exportedData.Count);

            return Analyze(exportedData);
        }
    }
}
