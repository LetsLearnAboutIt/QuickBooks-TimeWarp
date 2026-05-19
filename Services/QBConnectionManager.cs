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
            @"\Desktop\Air_Masters",
            // Legacy paths (with spaces) — still blocked for safety
            @"\Desktop\Joshua's Gold Coast",
            @"\Desktop\Blank Template",
            @"\Desktop\Air Masters",
            @"\Desktop\AirMasters"
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

        /// <summary>
        /// Opens a connection to QuickBooks Desktop via the QBXML Request Processor COM object.
        /// </summary>
        public void Connect()
        {
            try
            {
                // Re-validate path at connection time (defense in depth)
                ValidateCompanyFilePath(_config.CompanyFilePath);

                Log.Information("[{Instance}] Connecting to QuickBooks at: {FilePath}",
                    _instanceName, _config.CompanyFilePath);

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
                // Parameters: companyFile (empty = currently open file), fileMode
                // FileMode: doNotCare = 0, singleUser = 1, multiUser = 2
                _ticket = _requestProcessor.BeginSession(
                    _config.CompanyFilePath,      // Path to .QBW file (empty = current)
                    0                             // doNotCare mode
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
                Log.Error(ex, "[{Instance}] COM error processing QBXML request: {Message}",
                    _instanceName, ex.Message);
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
            string? fromDate = null, string? toDate = null)
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
