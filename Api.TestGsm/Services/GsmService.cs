using Microsoft.Extensions.Options;
using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Text;

namespace Api.TestGsm.Services;

public class GsmService : IGsmService, IDisposable
{
    private readonly SerialPort _serialPort;
    private readonly string _portName;
    private readonly ILogger<GsmService> _logger;
    private readonly GsmOptions _options;
    private readonly object _serialLock = new();
    private volatile bool _callCommandInProgress;
    private volatile bool _callActive;

    public GsmService(IOptions<GsmOptions> options, ILogger<GsmService> logger)
    {
        _logger = logger;

        _options = options.Value ?? new GsmOptions();

        var defaultPort = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? RequiredValue(_options.WindowsDefaultPortName, "Gsm:WindowsDefaultPortName")
            : RequiredValue(_options.LinuxDefaultPortName, "Gsm:LinuxDefaultPortName");
        _portName = string.IsNullOrWhiteSpace(_options.PortName) ? defaultPort : _options.PortName;

        _serialPort = new SerialPort(_portName)
        {
            BaudRate = RequiredValue(_options.BaudRate, "Gsm:BaudRate"),
            DataBits = RequiredValue(_options.DataBits, "Gsm:DataBits"),
            StopBits = TryParseEnum(_options.StopBits, StopBits.One),
            Parity = TryParseEnum(_options.Parity, Parity.None),
            Handshake = TryParseEnum(_options.Handshake, Handshake.None),
            NewLine = RequiredValue(_options.NewLine, "Gsm:NewLine"),
            ReadTimeout = RequiredValue(_options.ReadTimeout, "Gsm:ReadTimeout"),
            WriteTimeout = RequiredValue(_options.WriteTimeout, "Gsm:WriteTimeout")
        };
        _logger.LogInformation("Configured serial port {Port} BaudRate={BaudRate} DataBits={DataBits} StopBits={StopBits} Parity={Parity} Handshake={Handshake} NewLine={NewLine} ReadTimeout={ReadTimeout} WriteTimeout={WriteTimeout}",
            _portName, _serialPort.BaudRate, _serialPort.DataBits, _serialPort.StopBits, _serialPort.Parity, _serialPort.Handshake, _serialPort.NewLine, _serialPort.ReadTimeout, _serialPort.WriteTimeout);
        // Do not open here - open on demand to avoid errors on dev machines without the device
    }

    private static TEnum TryParseEnum<TEnum>(string? value, TEnum fallback) where TEnum : struct, Enum
    => Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : fallback;

    private static T RequiredValue<T>(T? value, string configurationKey)
        => value is null
            ? throw new InvalidOperationException($"Missing configuration value '{configurationKey}'.")
            : value;

    private static int RequiredValue(int? value, string configurationKey)
        => value
            ?? throw new InvalidOperationException($"Missing configuration value '{configurationKey}'.");

    private static string RequiredValue(string? value, string configurationKey)
        => string.IsNullOrEmpty(value)
            ? throw new InvalidOperationException($"Missing configuration value '{configurationKey}'.")
            : value;

    private int ResolveTimeout(GsmCommandStep step)
    {
        if (step.TimeoutMs.HasValue)
        {
            return step.TimeoutMs.Value;
        }

        return step.TimeoutKey?.Trim().ToUpperInvariant() switch
        {
            "2S" => _options.Timeouts.TwoSecondsMs,
            "5S" => _options.Timeouts.FiveSecondsMs,
            "10S" => _options.Timeouts.TenSecondsMs,
            "15S" => _options.Timeouts.FifteenSecondsMs,
            "25S" => _options.Timeouts.TwentyFiveSecondsMs,
            null or "" => _options.Timeouts.TwoSecondsMs,
            var key => throw new InvalidOperationException($"Unknown GSM timeout key '{key}'.")
        };
    }

    private void ExecuteSteps(IReadOnlyCollection<GsmCommandStep> steps, string configurationKey)
    {
        if (steps.Count == 0)
        {
            throw new InvalidOperationException($"Missing GSM command sequence '{configurationKey}'.");
        }

        foreach (var step in steps)
        {
            try
            {
                ExecuteStep(step, configurationKey);
            }
            catch when (step.ContinueOnFailure)
            {
                _logger.LogWarning("Configured GSM step failed but ContinueOnFailure=true for sequence {Sequence}", configurationKey);
            }
        }
    }

    private void TryExecuteConfiguredSteps(IReadOnlyCollection<GsmCommandStep> steps, string configurationKey)
    {
        if (steps.Count == 0)
        {
            return;
        }

        try
        {
            ExecuteSteps(steps, configurationKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Configured cleanup sequence {Sequence} failed", configurationKey);
        }
    }

    private void ExecuteStep(GsmCommandStep step, string configurationKey)
    {
        var command = step.Command ?? string.Empty;
        var timeoutMs = ResolveTimeout(step);

        if (!string.IsNullOrEmpty(step.Payload))
        {
            ExecI2cWrite(command, step.Payload, timeoutMs);
            return;
        }

        if (!string.IsNullOrEmpty(step.RequiredText))
        {
            ValidateCommandResponse(command, step.RequiredText, timeoutMs);
            return;
        }

        if (step.ExpectedResponses.Length == 0)
        {
            throw new InvalidOperationException($"Configured GSM step in '{configurationKey}' must define ExpectedResponses, Payload, or RequiredText.");
        }

        ExecGsmCommandAny(command, step.ExpectedResponses, timeoutMs);
    }

    private void EnsureOpen()
    {
        if (_serialPort.IsOpen) return;
        try
        {
            _serialPort.PortName = _portName;
            _logger.LogInformation("Attempting to open serial port {Port} with BaudRate={BaudRate}", _portName, _serialPort.BaudRate);
            _serialPort.Open();

            Thread.Sleep(RequiredValue(_options.OpenDelayMs, "Gsm:OpenDelayMs"));

            _logger.LogInformation("Opened serial port {Port}", _portName);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied to serial port {Port}. Port may be in use by another application.", _portName);
            throw new InvalidOperationException($"Serial port '{_portName}' is already in use or access is denied: {ex.Message}", ex);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Serial port {Port} not found or cannot be opened", _portName);
            throw new InvalidOperationException($"Serial port '{_portName}' not found or unavailable: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open serial port {Port}", _portName);
            throw new InvalidOperationException($"Failed to open serial port '{_portName}': {ex.Message}", ex);
        }
    }

    public void TestBenchInitialization()
    {
        lock (_serialLock)
        {
            try
            {
                _callActive = false;
                _logger.LogInformation("Starting test bench initialization");
                ExecuteSteps(_options.CommandSequences.TestBenchInitialization, "Gsm:CommandSequences:TestBenchInitialization");
                _logger.LogInformation("Test bench initialization completed");
            }
            catch
            {
                TryExecuteConfiguredSteps(_options.CommandSequences.TestBenchInitializationFailureCleanup, "Gsm:CommandSequences:TestBenchInitializationFailureCleanup");
                _logger.LogWarning("Initialization failed; ensured DTMF detection is disabled");
                throw;
            }
        }
    }

    public void FourGModuleInitialization()
    {
        lock (_serialLock)
        {
            try
            {
                _callActive = false;
                _logger.LogInformation("Starting 4G module initialization");

                ExecuteSteps(_options.CommandSequences.FourGModuleInitialization, "Gsm:CommandSequences:FourGModuleInitialization");

                _logger.LogInformation("4G module initialization completed");
            }
            catch
            {
                _logger.LogWarning("4G module initialization failed");
                throw;
            }
        }
    }

    public void LaunchTest(string phoneNumber, bool audioOk)
    {
        lock (_serialLock)
        {
            _logger.LogInformation("Launching test call to {PhoneNumber} (audioOk={AudioOk})", phoneNumber, audioOk);
            _callCommandInProgress = true;
            _callActive = false;

            try
            {
                ExecGsmCommandAny(
                    BuildVoiceDialCommand(phoneNumber),
                    RequiredValue(_options.CommandSequences.TestCallConnectedResponses, "Gsm:CommandSequences:TestCallConnectedResponses"),
                    _options.Timeouts.TwentyFiveSecondsMs);

                _callActive = true;
                _logger.LogInformation("Call to {PhoneNumber} connected", phoneNumber);

                if (audioOk)
                {
                    try
                    {
                        ExecuteSteps(_options.CommandSequences.AudioMarker, "Gsm:CommandSequences:AudioMarker");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Call is connected, but audio marker command failed");
                        TryExecuteConfiguredSteps(_options.CommandSequences.AudioMarkerFailureCleanup, "Gsm:CommandSequences:AudioMarkerFailureCleanup");
                    }
                }

                _logger.LogInformation("Test call completed successfully");
            }
            catch (Exception ex)
            {
                _callActive = false;
                _logger.LogError(ex, "Test call failed");
                TryExecuteConfiguredSteps(_options.CommandSequences.TestCallFailureCleanup, "Gsm:CommandSequences:TestCallFailureCleanup");
                _logger.LogWarning("Test call encountered an error; ensured DTMF detection is disabled");
                throw;
            }
            finally
            {
                _callCommandInProgress = false;
            }
        }
    }

    public void HangUpCall()
    {
        lock (_serialLock)
        {
            _logger.LogInformation("Hanging up call");
            ExecuteSteps(_options.CommandSequences.HangUp, "Gsm:CommandSequences:HangUp");
            _callActive = false;
            _callCommandInProgress = false;
        }
    }

    // Health
    public bool IsPortOpen() => _serialPort.IsOpen;

    public bool PingModem()
    {
        if (_callCommandInProgress || _callActive)
        {
            _logger.LogDebug("Skipping modem ping while a call is in progress or active");
            return _serialPort.IsOpen;
        }

        lock (_serialLock)
        {
            try
            {
                if (_callCommandInProgress || _callActive)
                {
                    _logger.LogDebug("Skipping modem ping while a call is in progress or active");
                    return _serialPort.IsOpen;
                }

                _logger.LogDebug("Pinging modem");

                // Don't ping if port is not open yet
                if (!_serialPort.IsOpen)
                {
                    _logger.LogDebug("Port not open, skipping ping");
                    return false;
                }

                ExecuteSteps(_options.CommandSequences.Ping, "Gsm:CommandSequences:Ping");
                _logger.LogInformation("Modem responded to ping");
                return true;
            }
            catch (TimeoutException ex)
            {
                _logger.LogDebug(ex, "Modem ping timeout (normal during active call)");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Modem did not respond to ping");
                return false;
            }
        }
    }

    private string BuildVoiceDialCommand(string phoneNumber)
    {
        var normalizedPhoneNumber = phoneNumber.Trim().TrimEnd(';');
        return string.Format(
            RequiredValue(_options.Protocol.VoiceDialCommandFormat, "Gsm:Protocol:VoiceDialCommandFormat"),
            normalizedPhoneNumber);
    }

    private void WriteOnSerialPort(byte[] data)
    {
        EnsureOpen();
        _serialPort.DiscardInBuffer();
        try
        {
            string text = Encoding.ASCII.GetString(data);
            _logger.LogDebug("TX -> {Port}: {Text}", _portName, text.Replace("\r", "\\r").Replace("\n", "\\n"));
            _serialPort.Write(data, 0, data.Length);
            _serialPort.BaseStream.Flush();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Serial write failed on {Port}", _portName);
            throw new InvalidOperationException($"Serial write failed: {ex.Message}", ex);
        }
    }

    private bool WaitResponse(out string response, out string error, int timeoutMs)
    {
        EnsureOpen();

        response = string.Empty;
        error = string.Empty;

        if (timeoutMs <= 0)
        {
            return true;
        }

        var deadline = Environment.TickCount64 + timeoutMs;
        var buffer = new List<byte>(128);
        byte last = 0;

        while (Environment.TickCount64 <= deadline)
        {
            try
            {
                // Check if data is available before attempting to read
                while (_serialPort.BytesToRead > 0)
                {
                    int b = _serialPort.ReadByte();
                    if (b < 0) break;
                    last = (byte)b;
                    buffer.Add((byte)b);
                    if (last == (byte)'\n')
                    {
                        // keep reading until we gathered as many lines as needed by ExecGsmCommand
                        // Here we stop at newline like the C++ inner loop
                        response = Encoding.ASCII.GetString(buffer.ToArray());
                        _logger.LogDebug("RX <- {Port}: {Text}", _portName, response.Replace("\r", "\\r").Replace("\n", "\\n"));
                        return true;
                    }
                }
            }
            catch (TimeoutException)
            {
                // ReadByte timed out, continue polling
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error reading from serial port {Port}", _portName);
                error = $"Read error: {ex.Message}";
                return false;
            }

            Thread.Sleep(RequiredValue(_options.ReadPollingDelayMs, "Gsm:ReadPollingDelayMs"));
        }

        error = "Error time out";
        _logger.LogWarning("RX timeout on {Port} after {TimeoutMs} ms", _portName, timeoutMs);
        return false;
    }

    private bool WaitForText(string expectedText, out string response, out string error, int timeoutMs)
    {
        EnsureOpen();

        response = string.Empty;
        error = string.Empty;

        if (timeoutMs <= 0)
        {
            return true;
        }

        var deadline = Environment.TickCount64 + timeoutMs;
        var buffer = new List<byte>(128);
        while (Environment.TickCount64 <= deadline)
        {
            while (_serialPort.BytesToRead > 0)
            {
                int b = _serialPort.ReadByte();
                if (b < 0) break;

                buffer.Add((byte)b);
                response = Encoding.ASCII.GetString(buffer.ToArray());
                if (response.Contains(expectedText, StringComparison.Ordinal))
                {
                    _logger.LogDebug("RX <- {Port}: {Text}", _portName, response.Replace("\r", "\\r").Replace("\n", "\\n"));
                    return true;
                }
            }

            Thread.Sleep(RequiredValue(_options.WaitForTextPollingDelayMs, "Gsm:WaitForTextPollingDelayMs"));
        }

        error = "Error time out";
        _logger.LogWarning("RX timeout waiting for {ExpectedText} on {Port} after {TimeoutMs} ms", expectedText, _portName, timeoutMs);
        return false;
    }

    private static string NormalizeNoWhitespace(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (!char.IsWhiteSpace(ch)) sb.Append(ch);
        }
        return sb.ToString();
    }

    private void ExecGsmCommand(string command, string expectedResponse, int receiveTimeOut)
        => ExecGsmCommandAny(command, new[] { expectedResponse }, receiveTimeOut);

    private void ExecGsmCommandAny(string command, string[] expectedResponses, int receiveTimeOut)
    {
        // Ensure port is open
        if (!string.IsNullOrEmpty(command) || (expectedResponses?.Length ?? 0) > 0)
        {
            EnsureOpen();
        }

        var respBuilder = new StringBuilder();

        if (!string.IsNullOrEmpty(command))
        {
            WriteOnSerialPort(Encoding.ASCII.GetBytes(command));
        }

        if (expectedResponses != null && expectedResponses.Length > 0)
        {
            // Read with slack to absorb extra echoes or blank lines
            var expectedLines = expectedResponses.Max(r => r.Count(c => c == '\n'));
            var maxLinesToRead = expectedLines + RequiredValue(_options.ExecGsmCommandSlackLines, "Gsm:ExecGsmCommandSlackLines");

            string? matchedVariant = null;
            for (int i = 0; i < maxLinesToRead; i++)
            {
                if (WaitResponse(out var read, out var err, receiveTimeOut))
                {
                    respBuilder.Append(read);

                    var aggregated = respBuilder.ToString();
                    var normAggregated = NormalizeNoWhitespace(aggregated);

                    foreach (var exp in expectedResponses)
                    {
                        var normExpLocal = NormalizeNoWhitespace(exp);
                        if (normAggregated.Contains(normExpLocal, StringComparison.Ordinal))
                        {
                            matchedVariant = exp;
                            break;
                        }
                    }

                    if (matchedVariant != null)
                    {
                        _logger.LogDebug("Response matched for [{Command}]", command.Replace("\r", "\\r"));
                        return;
                    }
                }
                else
                {
                    _logger.LogError("Command timeout for [{Command}] after {TimeoutMs} ms", command.Replace("\r", "\\r"), receiveTimeOut);
                    throw new InvalidOperationException($"{err} [{command}]");
                }
            }

            // No match after slack lines
            string resp = respBuilder.ToString();
            var normResp = resp.Replace("\r", "\\r").Replace("\n", "\\n");
            var normExp = string.Join(" || ", expectedResponses.Select(e => e.Replace("\r", "\\r").Replace("\n", "\\n")));
            _logger.LogError("Incorrect response for [{Command}] => got: {Got} expected: {Expected}", command.Replace("\r", "\\r"), normResp, normExp);
            throw new InvalidOperationException($"incorrect response: {normResp}<>${normExp}");
        }
    }

    private void ExecI2cWrite(string command, string payload, int receiveTimeOut)
    {
        EnsureOpen();
        WriteOnSerialPort(Encoding.ASCII.GetBytes(command));

        if (!WaitForText(RequiredValue(_options.Protocol.I2cPrompt, "Gsm:Protocol:I2cPrompt"), out var promptResponse, out var promptError, receiveTimeOut))
        {
            _logger.LogError("I2C write prompt timeout for [{Command}] after {TimeoutMs} ms", command.Replace("\r", "\\r"), receiveTimeOut);
            throw new InvalidOperationException($"{promptError} [{command}]");
        }

        _logger.LogDebug("I2C write prompt received for [{Command}]: {Response}", command.Replace("\r", "\\r"), promptResponse.Replace("\r", "\\r").Replace("\n", "\\n"));
        ExecGsmCommand(payload, RequiredValue(_options.Protocol.OkResponse, "Gsm:Protocol:OkResponse"), receiveTimeOut);
    }

    private void ValidateCommandResponse(string command, string requiredText, int totalTimeoutMs)
    {
        var response = ExecAndRead(command, totalTimeoutMs);

        _logger.LogDebug("ValidateCommandResponse - Command: {Command}, RequiredText: {RequiredText}, Response: {Response}",
            command.Replace("\r", "\\r"),
            requiredText,
            response.Replace("\r", "\\r").Replace("\n", "\\n"));

        if (!response.Contains(requiredText, StringComparison.OrdinalIgnoreCase) ||
            !response.Contains(RequiredValue(_options.Protocol.OkResponse, "Gsm:Protocol:OkResponse"), StringComparison.Ordinal))
        {
            _logger.LogError(
                "Incorrect response for [{Command}] => got: {Got} expected text: {ExpectedText} and OK",
                command.Replace("\r", "\\r"),
                response.Replace("\r", "\\r").Replace("\n", "\\n"),
                requiredText);
            throw new InvalidOperationException($"incorrect response: {response}<>${requiredText}");
        }

        _logger.LogInformation("Command {Command} validated successfully", command.Replace("\r", "\\r"));
    }

    // Helpers
    private string ExecAndRead(string command, int totalTimeoutMs)
    {
        EnsureOpen();
        if (!string.IsNullOrEmpty(command))
        {
            WriteOnSerialPort(Encoding.ASCII.GetBytes(command));
        }

        var sb = new StringBuilder();
        var deadline = Environment.TickCount64 + totalTimeoutMs;
        var iterations = 0;
        var maxIterations = RequiredValue(_options.ExecAndReadMaxIterations, "Gsm:ExecAndReadMaxIterations");

        while (Environment.TickCount64 <= deadline && iterations < maxIterations)
        {
            iterations++;

            try
            {
                if (WaitResponse(out var line, out _, RequiredValue(_options.ExecAndReadLineTimeoutMs, "Gsm:ExecAndReadLineTimeoutMs")))
                {
                    sb.Append(line);
                    var agg = sb.ToString();
                    if (agg.Contains(RequiredValue(_options.Protocol.OkResponse, "Gsm:Protocol:OkResponse"), StringComparison.Ordinal) ||
                        agg.Contains(RequiredValue(_options.Protocol.ErrorResponse, "Gsm:Protocol:ErrorResponse"), StringComparison.Ordinal))
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ExecAndRead iteration {Iteration} error", iterations);
            }
        }

        var res = sb.ToString();
        _logger.LogDebug("ExecAndRead aggregated after {Iterations} iterations: {Text}",
            iterations,
            res.Replace("\r", "\\r").Replace("\n", "\\n"));
        return res;
    }

    private static bool TryParseClccStat(string line, string clccPrefix, out int stat)
    {
        stat = -1;
        var idx = line.IndexOf(clccPrefix, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return false;
        var rest = line[(idx + clccPrefix.Length)..];
        var tokens = rest.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length >= 3 && int.TryParse(tokens[2], out stat)) return true;
        return false;
    }

    private bool WaitForVoiceConnected(int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        var callAttempts = 0;
        var maxCallAttempts = RequiredValue(_options.CallStatusMaxAttempts, "Gsm:CallStatusMaxAttempts");

        while (Environment.TickCount64 <= deadline && callAttempts < maxCallAttempts)
        {
            callAttempts++;

            try
            {
                var statusSteps = _options.CommandSequences.WaitForVoiceConnectedStatus;
                var statusStep = statusSteps.Count > 0
                    ? statusSteps[0]
                    : throw new InvalidOperationException("Missing GSM command sequence 'Gsm:CommandSequences:WaitForVoiceConnectedStatus'.");
                var resp = ExecAndRead(RequiredValue(statusStep.Command, "Gsm:CommandSequences:WaitForVoiceConnectedStatus:0:Command"), ResolveTimeout(statusStep));

                _logger.LogDebug("WaitForVoiceConnected attempt {Attempt}: {Response}",
                    callAttempts,
                    resp.Replace("\r", "\\r").Replace("\n", "\\n"));

                foreach (var line in resp.Split('\n'))
                {
                    if (line.Contains(RequiredValue(_options.Protocol.ClccPrefix, "Gsm:Protocol:ClccPrefix"), StringComparison.OrdinalIgnoreCase) &&
                        TryParseClccStat(line, RequiredValue(_options.Protocol.ClccPrefix, "Gsm:Protocol:ClccPrefix"), out var stat))
                    {
                        // stat:0=active,2=dialing,3=alerting,4=incoming,5=waiting
                        if (stat == 0)
                        {
                            _logger.LogInformation("Call is connected (CLCC stat=0)");
                            return true;
                        }
                        else
                        {
                            _logger.LogDebug("Call status: stat={Stat}", stat);
                        }
                    }
                    if (line.Contains(RequiredValue(_options.Protocol.NoCarrierText, "Gsm:Protocol:NoCarrierText"), StringComparison.OrdinalIgnoreCase) ||
                        line.Contains(RequiredValue(_options.Protocol.BusyText, "Gsm:Protocol:BusyText"), StringComparison.OrdinalIgnoreCase) ||
                        line.Contains(RequiredValue(_options.Protocol.NoAnswerText, "Gsm:Protocol:NoAnswerText"), StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning("Call failed: {Line}", line.Trim());
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error checking call status on attempt {Attempt}", callAttempts);
            }

            Thread.Sleep(RequiredValue(_options.CallStatusPollingDelayMs, "Gsm:CallStatusPollingDelayMs"));
        }

        _logger.LogWarning("Call did not connect after {Attempts} attempts / {TimeoutMs}ms", callAttempts, timeoutMs);
        return false;
    }

    public void Dispose()
    {
        if (_serialPort.IsOpen)
        {
            try { _serialPort.Close(); _logger.LogInformation("Closed serial port {Port}", _portName); } catch { }
        }

        _serialPort.Dispose();
    }
}
