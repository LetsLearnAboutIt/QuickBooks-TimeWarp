using System.Diagnostics;
using System.IO;
using System.Xml.Linq;
using QB_TimeWarp.Models;
using Serilog;

namespace QB_TimeWarp.Services
{
    /// <summary>
    /// Manages connections to QuickBooks Desktop via QBXML SDK.
    /// Handles session lifecycle, authentication, and QBXML request/response processing.
    /// 
    /// IMPORTANT: This uses the QBXML processor (COM object) approach, which works with both
    /// QB 2023 (SDK 16.0) and QB 2021 (SDK 15.0). The actual COM interop requires:
    ///   - QuickBooks Desktop to be installed and running
    ///   - QuickBooks SDK installed
    ///   - Application authorized in QB (Edit > Preferences > Integrated Applications)
    /// </summary>
    public class QBConnectionManager : IDisposable
    {
        private readonly QBInstanceConfig _config;
        private readonly string _instanceName;
        private dynamic? _requestProcessor;
        private string? _ticket;
        private bool _isConnected;
        private bool _disposed;

        public bool IsConnected => _isConnected;
        public string InstanceName => _instanceName;

        /// <summary>
        /// List of protected Desktop folder path fragments.
        /// Connections to files in these locations will be REJECTED.
        /// </summary>
        private static readonly string[] ProtectedDesktopFolders = new[]
        {
            @"\Desktop\Joshs_Gold_Coast",
            @"\Desktop\QB21_Blank_Template",
            @"\Desktop\Client_Files",
            @"\Desktop\Air_Masters",
            // Legacy paths (with spaces) — still blocked for safety
            @"\Desktop\Joshua's Gold Coast",
            @"\Desktop\Blank Template",
            @"\Desktop\Air Masters"
        };

        public QBConnectionManager(QBInstanceConfig config, string instanceName)
        {
            _config = config;
            _instanceName = instanceName;

            // SAFETY: Validate the company file path is not an original Desktop file
            ValidateCompanyFilePath(config.CompanyFilePath);
        }

        /// <summary>
        /// Validates that the company file path does NOT point to a protected original file.
        /// Throws OriginalFileProtectionException if a protected path is detected.
        /// </summary>
        private void ValidateCompanyFilePath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return;

            var normalizedPath = filePath.ToUpperInvariant();

            foreach (var protectedFolder in ProtectedDesktopFolders)
            {
                if (normalizedPath.Contains(protectedFolder.ToUpperInvariant()))
                {
                    throw new OriginalFileProtectionException(
                        $"🛑 SAFETY BLOCK: QBConnectionManager [{_instanceName}] attempted to connect to " +
                        $"PROTECTED original file: {filePath}. " +
                        $"This path matches protected folder pattern '{protectedFolder}'. " +
                        "Only working copies in C:\\QB-TimeWarp\\Working\\ may be used. " +
                        "Ensure WorkingDirectoryManager has initialized working copies.");
                }
            }

            // Additional check: warn if path contains \Desktop\ but not \Working\
            if (normalizedPath.Contains("\\DESKTOP\\") && !normalizedPath.Contains("\\WORKING\\"))
            {
                Log.Warning("[{Instance}] ⚠ Company file path appears to be on Desktop: {Path}. " +
                    "Verify this is intentional and not an original file.",
                    _instanceName, filePath);
            }

            // Positive confirmation: log if we're correctly using a Working directory
            if (normalizedPath.Contains("\\WORKING\\"))
            {
                Log.Information("[{Instance}] ✓ Using working copy (originals protected): {Path}",
                    _instanceName, filePath);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  QuickBooks Launch — Process.Start the QB executable with a company file
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Known installation paths for QuickBooks Desktop (checked in order).
        /// </summary>
        private static readonly string[] QBInstallPaths = new[]
        {
            @"C:\Program Files\Intuit\QuickBooks 2023\QBW32.EXE",
            @"C:\Program Files (x86)\Intuit\QuickBooks 2023\QBW32.EXE",
            @"C:\Program Files\Intuit\QuickBooks 2023\QBW.EXE",
            @"C:\Program Files (x86)\Intuit\QuickBooks 2023\QBW.EXE",
            @"C:\Program Files\Intuit\QuickBooks Enterprise Solutions 23.0\QBW32.EXE",
            @"C:\Program Files (x86)\Intuit\QuickBooks Enterprise Solutions 23.0\QBW32.EXE",
        };

        /// <summary>
        /// Finds the QuickBooks executable on the local machine.
        /// Searches known installation paths in order.
        /// </summary>
        /// <returns>Full path to the QB executable, or null if not found.</returns>
        public static string? FindQuickBooksExecutable()
        {
            foreach (var path in QBInstallPaths)
            {
                if (File.Exists(path))
                {
                    Log.Information("Found QuickBooks executable: {Path}", path);
                    return path;
                }
            }

            Log.Warning("QuickBooks executable not found in any known location.");
            return null;
        }

        /// <summary>
        /// Launches QuickBooks Desktop WITHOUT opening a company file.
        /// FIX #52: QB ignores command-line file arguments in many versions, so we
        /// launch the application plain and let the SDK's BeginSession(filePath, ...)
        /// open the company file programmatically. This triggers QB's native password
        /// dialog (if the file is password-protected) and the certificate approval
        /// dialog (if the app hasn't been authorized yet) — all from a single
        /// BeginSession call, giving the user one unified prompt sequence.
        /// </summary>
        /// <returns>The launched Process, or null if QB executable was not found.</returns>
        public static Process? LaunchQuickBooks()
        {
            var qbExePath = FindQuickBooksExecutable();
            if (qbExePath == null)
            {
                Log.Error("Cannot launch QuickBooks — executable not found. " +
                    "Searched: {Paths}", string.Join(", ", QBInstallPaths));
                return null;
            }

            Log.Information("Launching QuickBooks (no file argument — SDK will open file): {Exe}", qbExePath);

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = qbExePath,
                    // FIX #52: No Arguments — QB ignores file args in many versions.
                    // The SDK's BeginSession() will open the file and trigger QB's
                    // native password + certificate dialogs.
                    UseShellExecute = true,          // Required for GUI apps
                    WindowStyle = ProcessWindowStyle.Normal
                };

                var process = Process.Start(startInfo);

                if (process != null)
                {
                    Log.Information("QuickBooks launched successfully (PID: {PID}). " +
                        "The SDK will open the company file during BeginSession().", process.Id);
                }
                else
                {
                    Log.Warning("Process.Start returned null — QuickBooks may not have launched.");
                }

                return process;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to launch QuickBooks: {Message}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Waits for QuickBooks to be ready to accept SDK connections.
        /// Polls by attempting to create the COM object until it succeeds or times out.
        /// FIX #51: Increased initial delay and added progress callback.
        /// </summary>
        /// <param name="timeoutSeconds">Maximum seconds to wait (default 90).</param>
        /// <param name="initialDelayMs">Initial delay before first poll (default 5000ms — gives QB time to start).</param>
        /// <param name="pollIntervalMs">Milliseconds between polling attempts (default 3000).</param>
        /// <param name="onProgress">Optional callback invoked each attempt with (attempt, elapsedSeconds, message).</param>
        /// <returns>True if QB appears ready; false if timed out.</returns>
        public static bool WaitForQuickBooksReady(
            int timeoutSeconds = 90,
            int initialDelayMs = 5000,
            int pollIntervalMs = 3000,
            Action<int, int, string>? onProgress = null)
        {
            Log.Information("Waiting up to {Timeout}s for QuickBooks to be ready " +
                "(initial delay: {Delay}ms)...", timeoutSeconds, initialDelayMs);

            // Initial delay — QB needs time to start its executable and load the COM server
            if (initialDelayMs > 0)
            {
                onProgress?.Invoke(0, 0, $"Waiting {initialDelayMs / 1000}s for QuickBooks to start...");
                Thread.Sleep(initialDelayMs);
            }

            var startTime = DateTime.UtcNow;
            var deadline = startTime.AddSeconds(timeoutSeconds);
            int attempt = 0;

            while (DateTime.UtcNow < deadline)
            {
                attempt++;
                var elapsed = (int)(DateTime.UtcNow - startTime).TotalSeconds;
                var remaining = timeoutSeconds - elapsed;

                try
                {
                    var qbType = Type.GetTypeFromProgID("QBXMLRP2.RequestProcessor");
                    if (qbType != null)
                    {
                        var rp = Activator.CreateInstance(qbType);
                        if (rp != null)
                        {
                            // COM object created — QB SDK is available
                            System.Runtime.InteropServices.Marshal.ReleaseComObject(rp);
                            Log.Information("QuickBooks is ready (attempt {Attempt}, {Elapsed}s elapsed)",
                                attempt, elapsed);
                            onProgress?.Invoke(attempt, elapsed, "QuickBooks is ready.");
                            return true;
                        }
                    }
                }
                catch
                {
                    // QB not ready yet — continue polling
                }

                var msg = $"Waiting for QuickBooks... ({remaining}s remaining)";
                onProgress?.Invoke(attempt, elapsed, msg);
                Log.Debug("QB not ready yet (attempt {Attempt}, {Elapsed}s), retrying in {Interval}ms...",
                    attempt, elapsed, pollIntervalMs);
                Thread.Sleep(pollIntervalMs);
            }

            var totalElapsed = (int)(DateTime.UtcNow - startTime).TotalSeconds;
            Log.Warning("Timed out waiting for QuickBooks after {Elapsed}s ({Attempts} attempts)",
                totalElapsed, attempt);
            onProgress?.Invoke(attempt, totalElapsed, "Timed out waiting for QuickBooks.");
            return false;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Silent Certification Test — FIX #51
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Result of a silent connection test.
        /// </summary>
        public enum CertificationStatus
        {
            /// <summary>App is certified — SDK connected successfully, no UI needed.</summary>
            Certified,
            /// <summary>App is NOT certified — certificate dialog will appear on next attempt.</summary>
            NotCertified,
            /// <summary>QB SDK is not installed or COM object unavailable.</summary>
            SdkNotAvailable,
            /// <summary>Some other error occurred (file not found, QB not running, etc.).</summary>
            OtherError
        }

        /// <summary>
        /// Attempts a silent SDK connection to test whether this application is already
        /// certified (authorized) for the specified company file.
        ///
        /// FIX #51: If the app is already certified with "Yes, always allow access even if
        /// QuickBooks is not running", the SDK can connect without any user interaction.
        /// This test determines whether we need the full password dialog + QB launch flow.
        ///
        /// The connection is immediately closed after testing — this is a probe only.
        /// </summary>
        /// <param name="config">The QB instance config to test.</param>
        /// <param name="instanceName">Name for logging.</param>
        /// <returns>CertificationStatus indicating whether the app is certified.</returns>
        public static CertificationStatus TestCertification(QBInstanceConfig config, string instanceName)
        {
            Log.Information("[{Instance}] Testing silent certification for: {Path}",
                instanceName, config.CompanyFilePath);

            dynamic? rp = null;
            string? ticket = null;

            try
            {
                var qbType = Type.GetTypeFromProgID("QBXMLRP2.RequestProcessor");
                if (qbType == null)
                {
                    Log.Warning("[{Instance}] SDK not available — QBXMLRP2.RequestProcessor not found.",
                        instanceName);
                    return CertificationStatus.SdkNotAvailable;
                }

                rp = Activator.CreateInstance(qbType);
                if (rp == null)
                {
                    Log.Warning("[{Instance}] Failed to create RequestProcessor instance.", instanceName);
                    return CertificationStatus.SdkNotAvailable;
                }

                // OpenConnection2 with localQBD (1)
                rp.OpenConnection2("", config.ApplicationName, 1);

                // Try BeginSession — this is where the certificate check happens.
                // If the app is certified, this succeeds silently.
                // If NOT certified, QB shows the certificate dialog (which blocks / fails).
                const int qbFileOpenDoNotCare = 0;
                ticket = rp.BeginSession(config.CompanyFilePath, qbFileOpenDoNotCare);

                Log.Information("[{Instance}] ✓ Silent connection succeeded — app IS certified. " +
                    "Ticket: {Ticket}", instanceName, ticket);
                return CertificationStatus.Certified;
            }
            catch (System.Runtime.InteropServices.COMException ex)
            {
                // Common error codes for certification issues:
                // 0x80040408 — "This application has not been authorized"
                // 0x8004041D — "This application is not allowed to log into this QuickBooks company data file automatically"
                // 0x8004041A — "This application does not have permission to access this QuickBooks company data file"
                var hresult = ex.HResult;
                var hexCode = $"0x{hresult:X8}";

                Log.Information("[{Instance}] Silent connection failed: {HexCode} — {Message}",
                    instanceName, hexCode, ex.Message);

                // These are the "not authorized" family of errors
                if (hresult == unchecked((int)0x80040408) ||
                    hresult == unchecked((int)0x8004041D) ||
                    hresult == unchecked((int)0x8004041A) ||
                    ex.Message.Contains("authorized", StringComparison.OrdinalIgnoreCase) ||
                    ex.Message.Contains("permission", StringComparison.OrdinalIgnoreCase) ||
                    ex.Message.Contains("certificate", StringComparison.OrdinalIgnoreCase))
                {
                    return CertificationStatus.NotCertified;
                }

                // "Could not start QuickBooks" — QB not running and auto-launch not allowed
                if (ex.Message.Contains("Could not start QuickBooks", StringComparison.OrdinalIgnoreCase))
                {
                    return CertificationStatus.NotCertified;
                }

                return CertificationStatus.OtherError;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[{Instance}] Unexpected error during certification test: {Message}",
                    instanceName, ex.Message);
                return CertificationStatus.OtherError;
            }
            finally
            {
                // Always clean up the probe connection
                try
                {
                    if (rp != null && ticket != null)
                        rp.EndSession(ticket);
                }
                catch { /* ignore cleanup errors */ }

                try
                {
                    if (rp != null)
                        rp.CloseConnection();
                }
                catch { /* ignore cleanup errors */ }

                try
                {
                    if (rp != null)
                        System.Runtime.InteropServices.Marshal.ReleaseComObject(rp);
                }
                catch { /* ignore cleanup errors */ }
            }
        }

        /// <summary>
        /// Attempts to connect to QuickBooks with retries, allowing time for the user
        /// to approve the certificate/application authorization dialog in the QB UI.
        ///
        /// FIX #51: Uses exponential backoff (5s → 8s → 12s → ...) instead of fixed interval.
        /// Supports a progress callback so the UI can show countdown messages.
        /// Default timeout increased to 180s (3 minutes) for certificate approval.
        /// </summary>
        /// <param name="maxWaitSeconds">Maximum time to wait for successful connection (default 180).</param>
        /// <param name="onProgress">Optional callback: (attempt, elapsedSec, remainingSec, message).</param>
        public void ConnectWithCertificateWait(int maxWaitSeconds = 180,
            Action<int, int, int, string>? onProgress = null)
        {
            var startTime = DateTime.UtcNow;
            var deadline = startTime.AddSeconds(maxWaitSeconds);
            int attempt = 0;
            Exception? lastException = null;

            // Exponential backoff: 5s, 8s, 12s, 18s, 25s, then cap at 30s
            int currentIntervalMs = 5000;
            const int MaxIntervalMs = 30_000;
            const double BackoffMultiplier = 1.5;

            Log.Information("[{Instance}] Attempting connection with certificate wait " +
                "(up to {MaxWait}s, exponential backoff)...", _instanceName, maxWaitSeconds);

            while (DateTime.UtcNow < deadline)
            {
                attempt++;
                var elapsed = (int)(DateTime.UtcNow - startTime).TotalSeconds;
                var remaining = Math.Max(0, maxWaitSeconds - elapsed);

                onProgress?.Invoke(attempt, elapsed, remaining,
                    $"Connection attempt {attempt}... ({remaining}s remaining)");

                try
                {
                    Connect();
                    Log.Information("[{Instance}] Connected successfully on attempt {Attempt} " +
                        "({Elapsed}s elapsed)", _instanceName, attempt, elapsed);
                    onProgress?.Invoke(attempt, elapsed, remaining,
                        "✓ Connected to QuickBooks successfully!");
                    return; // Success!
                }
                catch (QBConnectionException ex)
                {
                    lastException = ex;
                    Log.Debug("[{Instance}] Attempt {Attempt} failed ({Elapsed}s): {Message}. " +
                        "User may need to approve certificate in QuickBooks.",
                        _instanceName, attempt, elapsed, ex.Message);
                    onProgress?.Invoke(attempt, elapsed, remaining,
                        "Waiting for certificate approval in QuickBooks...");
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    Log.Debug("[{Instance}] Attempt {Attempt} failed ({Elapsed}s): {Message}",
                        _instanceName, attempt, elapsed, ex.Message);
                }

                // Clean up partial connection state before retrying
                try { Disconnect(); } catch { /* ignore cleanup errors */ }

                // Check if we have time for another wait
                if (DateTime.UtcNow.AddMilliseconds(currentIntervalMs) > deadline)
                    break;

                Thread.Sleep(currentIntervalMs);

                // Exponential backoff
                currentIntervalMs = Math.Min(
                    (int)(currentIntervalMs * BackoffMultiplier),
                    MaxIntervalMs);
            }

            var totalElapsed = (int)(DateTime.UtcNow - startTime).TotalSeconds;
            Log.Error("[{Instance}] Failed to connect after {Attempts} attempts over {Elapsed}s. " +
                "Last error: {Message}", _instanceName, attempt, totalElapsed,
                lastException?.Message ?? "Unknown error");

            throw new QBConnectionException(
                $"Could not connect to QuickBooks ({_instanceName}) after {attempt} attempts " +
                $"over {totalElapsed}s. Ensure the certificate was approved in QuickBooks. " +
                $"Last error: {lastException?.Message}", lastException);
        }

        /// <summary>
        /// When true, the next Connect() call will first try BeginSession with an
        /// empty company file path (= attach to the currently-open file in QuickBooks).
        /// This avoids a duplicate password prompt when QuickBooks has already been
        /// launched WITH a file and the user has already authenticated.
        ///
        /// FIX #50: The QBXML SDK does NOT accept a password parameter — security is
        /// handled entirely by the QuickBooks Desktop UI.
        ///
        /// FIX #52: Since we now launch QB WITHOUT a file argument (QB ignores them),
        /// this property is typically false. The SDK's BeginSession(filePath, 0) opens
        /// the file and triggers QB's native password + certificate dialogs. Only set
        /// this to true if QB already has a file open (e.g., user opened it manually).
        /// </summary>
        public bool PreferCurrentlyOpenFile { get; set; }

        /// <summary>
        /// Opens a connection to QuickBooks Desktop via the QBXML Request Processor COM object.
        ///
        /// FIX #50 — Password handling strategy:
        /// The QB SDK BeginSession() does NOT accept a password parameter. When a company
        /// file is password-protected, QB shows its own login dialog.
        ///
        /// FIX #52 — File opening strategy:
        /// QB is launched WITHOUT a file argument (it ignores command-line file args).
        /// BeginSession(filePath, 0) opens the company file in the running QB instance,
        /// triggering the native password dialog and certificate approval in one step.
        /// If PreferCurrentlyOpenFile is true, we first try BeginSession("", 0) to
        /// attach to an already-open file; if that fails, we fall back to the file path.
        /// </summary>
        public void Connect()
        {
            try
            {
                // Re-validate path at connection time (defense in depth)
                ValidateCompanyFilePath(_config.CompanyFilePath);

                Log.Information("[{Instance}] Connecting to QuickBooks at: {FilePath} " +
                    "(PreferCurrentlyOpenFile={Prefer})",
                    _instanceName, _config.CompanyFilePath, PreferCurrentlyOpenFile);

                // Create the QBXML Request Processor COM object
                // Type: QBXMLRP2.RequestProcessor
                var qbType = Type.GetTypeFromProgID("QBXMLRP2.RequestProcessor");
                if (qbType == null)
                {
                    throw new InvalidOperationException(
                        "QuickBooks SDK not found. Ensure QuickBooks Desktop and the QBXML SDK are installed. " +
                        "The COM object 'QBXMLRP2.RequestProcessor' must be registered.");
                }

                _requestProcessor = Activator.CreateInstance(qbType);
                if (_requestProcessor == null)
                {
                    throw new InvalidOperationException("Failed to create QBXML Request Processor instance.");
                }

                // Open connection to QuickBooks
                // Parameters: appID (empty string), appName, connection type
                // Connection type: localQBD = 1, remoteQBD = 2, localQBDLaunchUI = 3
                _requestProcessor.OpenConnection2(
                    "",                          // App ID (not used for desktop)
                    _config.ApplicationName,     // Application name shown in QB
                    1                            // localQBD - connect to local QB Desktop
                );

                // Begin session with company file
                // FIX #44: qbFileOpenDoNotCare (0) is the "accountant override" mode —
                // it grants access to payroll data (Paychecks, PayrollItems, etc.)
                // even without an active payroll subscription on the target company.
                // Values: qbFileOpenDoNotCare = 0, singleUser = 1, multiUser = 2
                const int qbFileOpenDoNotCare = 0;

                // FIX #50: Try connecting to the currently-open file first to avoid
                // a duplicate password prompt. The QB SDK does NOT support passing
                // passwords programmatically — BeginSession("", 0) attaches to whatever
                // file is already open in the running QB instance.
                if (PreferCurrentlyOpenFile)
                {
                    try
                    {
                        Log.Information("[{Instance}] Trying BeginSession with currently-open file " +
                            "(empty path) to avoid duplicate password prompt...", _instanceName);
                        _ticket = _requestProcessor.BeginSession("", qbFileOpenDoNotCare);
                        _isConnected = true;
                        Log.Information("[{Instance}] Connected to currently-open file. " +
                            "Session ticket: {Ticket}", _instanceName, _ticket);
                        return;
                    }
                    catch (System.Runtime.InteropServices.COMException ex)
                    {
                        Log.Information("[{Instance}] Empty-path session failed ({Message}). " +
                            "Falling back to explicit file path...", _instanceName, ex.Message);
                        // Fall through to try with explicit path
                    }
                }

                // Standard path: pass the company file path to BeginSession.
                // If the file is password-protected and not already open,
                // QuickBooks will show its login dialog.
                _ticket = _requestProcessor.BeginSession(
                    _config.CompanyFilePath,      // Path to .QBW file
                    qbFileOpenDoNotCare           // accountant override — allows payroll access
                );

                _isConnected = true;
                Log.Information("[{Instance}] Successfully connected to QuickBooks. Session ticket: {Ticket}",
                    _instanceName, _ticket);
            }
            catch (System.Runtime.InteropServices.COMException ex)
            {
                Log.Error(ex, "[{Instance}] COM error connecting to QuickBooks. " +
                    "Ensure QuickBooks is running and the company file is accessible. Error: {Message}",
                    _instanceName, ex.Message);
                throw new QBConnectionException(
                    $"Failed to connect to QuickBooks ({_instanceName}): {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[{Instance}] Error connecting to QuickBooks: {Message}",
                    _instanceName, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Sends a QBXML request and returns the response XML.
        /// </summary>
        public string ProcessRequest(string qbxmlRequest)
        {
            EnsureConnected();

            try
            {
                Log.Debug("[{Instance}] Sending QBXML request ({Length} chars)",
                    _instanceName, qbxmlRequest.Length);

                string response = _requestProcessor!.ProcessRequest(_ticket, qbxmlRequest);

                Log.Debug("[{Instance}] Received QBXML response ({Length} chars)",
                    _instanceName, response.Length);

                // Check for errors in the response
                ValidateResponse(response);

                return response;
            }
            catch (System.Runtime.InteropServices.COMException ex)
            {
                Log.Error("[{Instance}] COM error processing QBXML request: {Message}",
                    _instanceName, ex.Message);
                Log.Error("[{Instance}] Failed QBXML request:\n{Request}", _instanceName, qbxmlRequest);
                throw new QBRequestException(
                    $"QBXML request failed ({_instanceName}): {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Sends a QBXML request with retry logic.
        /// Does NOT retry for SDK version compatibility errors (0x80040400) —
        /// those won't be fixed by retrying and should fail fast.
        /// </summary>
        public string ProcessRequestWithRetry(string qbxmlRequest)
        {
            int attempt = 0;
            while (true)
            {
                try
                {
                    attempt++;
                    return ProcessRequest(qbxmlRequest);
                }
                catch (System.Runtime.InteropServices.COMException comEx)
                    when (Helpers.QBSDKVersionHelper.IsUnsupportedElementCOMError(comEx.HResult))
                {
                    // Don't retry version compatibility errors — they won't fix themselves
                    Log.Warning("[{Instance}] SDK version compatibility error (0x80040400): {Message}. NOT retrying.",
                        _instanceName, comEx.Message);
                    throw new QBRequestException(
                        $"SDK version incompatibility ({_instanceName}): {comEx.Message}. " +
                        "This field/element is not supported in this QuickBooks version.", comEx);
                }
                catch (Exception ex) when (attempt < _config.MaxRetries)
                {
                    Log.Warning("[{Instance}] Request failed (attempt {Attempt}/{MaxRetries}): {Message}. Retrying...",
                        _instanceName, attempt, _config.MaxRetries, ex.Message);
                    Thread.Sleep(1000 * attempt); // Exponential backoff
                }
            }
        }

        /// <summary>
        /// Builds a complete QBXML request envelope around inner request XML.
        /// </summary>
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

        /// <summary>
        /// Builds a query request for a specific entity type.
        /// </summary>
        public static string BuildQueryRequest(string queryType, string sdkVersion = "16.0",
            bool includeInactive = true, int? maxReturned = null,
            string? fromDate = null, string? toDate = null,
            bool includeLineItems = false)
        {
            var innerXml = $"<{queryType}Rq>";

            if (maxReturned.HasValue)
                innerXml += $"<MaxReturned>{maxReturned.Value}</MaxReturned>";

            if (includeInactive)
                innerXml += "<ActiveStatus>All</ActiveStatus>";

            // Date range filter for transactions
            if (!string.IsNullOrEmpty(fromDate) || !string.IsNullOrEmpty(toDate))
            {
                innerXml += "<TxnDateRangeFilter>";
                if (!string.IsNullOrEmpty(fromDate))
                    innerXml += $"<FromTxnDate>{fromDate}</FromTxnDate>";
                if (!string.IsNullOrEmpty(toDate))
                    innerXml += $"<ToTxnDate>{toDate}</ToTxnDate>";
                innerXml += "</TxnDateRangeFilter>";
            }

            // ══════════════════════════════════════════════════════════
            // FIX #11: Include line items in transaction query responses
            // ══════════════════════════════════════════════════════════
            // QBXML transaction queries (CheckQuery, DepositQuery,
            // JournalEntryQuery, SalesReceiptQuery, InvoiceQuery, etc.)
            // do NOT return line items by default. Without this flag,
            // the response contains only header fields — no
            // ExpenseLineRet, DepositLineRet, JournalDebitLineRet, etc.
            //
            // This caused 100% failure (0/1,467) on all line-item-bearing
            // transaction types: entity.LineItems was empty, so
            // BuildLineItemsXml returned empty string, and QB 2021
            // rejected the Add request with either:
            //   - Error 3150: "missing element: DepositLineAdd"
            //   - 0x80040400: XML parse error (no content after header)
            //
            // The fix: <IncludeLineItems>true</IncludeLineItems>
            if (includeLineItems)
                innerXml += "<IncludeLineItems>true</IncludeLineItems>";

            innerXml += $"</{queryType}Rq>";

            return BuildQBXMLRequest(innerXml, sdkVersion);
        }

        /// <summary>
        /// Builds an Add request for a specific entity type.
        /// </summary>
        public static string BuildAddRequest(string addType, string innerFieldsXml, string sdkVersion = "15.0")
        {
            var innerXml = $@"<{addType}Rq>
    <{addType.Replace("Rq", "")}>
      {innerFieldsXml}
    </{addType.Replace("Rq", "")}>
  </{addType}Rq>";

            return BuildQBXMLRequest(innerXml, sdkVersion);
        }

        /// <summary>
        /// Parses the QBXML response and extracts entity data as XElements.
        /// </summary>
        public static List<XElement> ParseResponseEntities(string qbxmlResponse, string responseType)
        {
            var results = new List<XElement>();

            try
            {
                var doc = XDocument.Parse(qbxmlResponse);
                var responseElements = doc.Descendants(responseType);

                foreach (var element in responseElements)
                {
                    // Check status code
                    var statusCode = element.Parent?.Attribute("statusCode")?.Value;
                    if (statusCode == "0" || statusCode == null) // 0 = success
                    {
                        results.Add(element);
                    }
                    else
                    {
                        var statusMessage = element.Parent?.Attribute("statusMessage")?.Value;
                        Log.Warning("QBXML response status {Code}: {Message} for {Type}",
                            statusCode, statusMessage, responseType);
                    }
                }

                // Also try getting from Rs (response set) elements
                if (!results.Any())
                {
                    var rsElements = doc.Descendants($"{responseType.Replace("Ret", "")}Rs");
                    foreach (var rs in rsElements)
                    {
                        var statusCode = rs.Attribute("statusCode")?.Value;
                        if (statusCode == "0")
                        {
                            results.AddRange(rs.Elements(responseType));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error parsing QBXML response for {Type}: {Message}",
                    responseType, ex.Message);
            }

            return results;
        }

        /// <summary>
        /// Validates QBXML response for errors.
        /// </summary>
        private void ValidateResponse(string response)
        {
            try
            {
                var doc = XDocument.Parse(response);
                var statusCodes = doc.Descendants()
                    .Where(e => e.Attribute("statusCode") != null)
                    .Select(e => new
                    {
                        Code = e.Attribute("statusCode")?.Value,
                        Message = e.Attribute("statusMessage")?.Value,
                        Severity = e.Attribute("statusSeverity")?.Value
                    });

                foreach (var status in statusCodes)
                {
                    if (status.Code != "0" && status.Severity == "Error")
                    {
                        Log.Warning("[{Instance}] QBXML Error {Code}: {Message}",
                            _instanceName, status.Code, status.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not validate QBXML response: {Message}", ex.Message);
            }
        }

        private void EnsureConnected()
        {
            if (!_isConnected || _requestProcessor == null || _ticket == null)
            {
                throw new QBConnectionException(
                    $"Not connected to QuickBooks ({_instanceName}). Call Connect() first.");
            }
        }

        /// <summary>
        /// Disconnects from QuickBooks and releases resources.
        /// </summary>
        public void Disconnect()
        {
            try
            {
                if (_requestProcessor != null && _ticket != null)
                {
                    _requestProcessor.EndSession(_ticket);
                    Log.Information("[{Instance}] Session ended.", _instanceName);
                }

                if (_requestProcessor != null)
                {
                    _requestProcessor.CloseConnection();
                    Log.Information("[{Instance}] Connection closed.", _instanceName);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[{Instance}] Error during disconnect: {Message}",
                    _instanceName, ex.Message);
            }
            finally
            {
                _isConnected = false;
                _ticket = null;

                if (_requestProcessor != null)
                {
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(_requestProcessor);
                    _requestProcessor = null;
                }
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Disconnect();
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }

        ~QBConnectionManager()
        {
            Dispose();
        }
    }

    /// <summary>
    /// Exception thrown when QuickBooks connection fails.
    /// </summary>
    public class QBConnectionException : Exception
    {
        public QBConnectionException(string message) : base(message) { }
        public QBConnectionException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Exception thrown when a QBXML request fails.
    /// </summary>
    public class QBRequestException : Exception
    {
        public QBRequestException(string message) : base(message) { }
        public QBRequestException(string message, Exception inner) : base(message, inner) { }
    }
}
