using Microsoft.Extensions.Options;

namespace Api.TestGsm.Services;

/// <summary>
/// Simulates a GSM modem without requiring physical serial port hardware.
/// Useful for development and testing scenarios.
/// </summary>
public class GsmSimulatorService : IGsmService, IDisposable
{
    private readonly ILogger<GsmSimulatorService> _logger;
    private readonly GsmSimulatorOptions _options;
    private bool _isInitialized;
    private bool _isInCall;
    private string? _currentPhoneNumber;
    private DateTime? _callStartTime;
    private readonly Random _random = new();

    public GsmSimulatorService(IOptions<GsmSimulatorOptions> options, ILogger<GsmSimulatorService> logger)
    {
        _logger = logger;
        _options = options.Value ?? new GsmSimulatorOptions();

        _logger.LogInformation(
            "GSM Simulator initialized. SimulateFailures={SimulateFailures}, CallConnectDelayMs={CallConnectDelayMs}",
            _options.SimulateFailures,
            _options.CallConnectDelayMs);
    }

    public void TestBenchInitialization()
    {
        _logger.LogInformation("Starting simulated test bench initialization");

        if (_options.SimulateFailures && _random.Next(100) < _options.FailurePercentage)
        {
            _logger.LogError("Simulated initialization failure");
            throw new InvalidOperationException("Simulated modem initialization failure");
        }

        // Simulate AT command processing delay
        Thread.Sleep(_options.CommandDelayMs);

        // Simulate the sequence of AT commands
        SimulateCommand("AT");
        SimulateCommand("AT+CMEE=2");
        SimulateCommand("AT+CPIN?");
        SimulateCommand("ATZ");
        SimulateCommand("ATE0&W");
        SimulateCommand("AT+CRC=1");
        SimulateCommand("AT+COLP=0");
        SimulateCommand("AT+DDET=0");
        SimulateCommand("AT+CHFA=2");
        SimulateCommand("AT+SIDET=2,0");
        SimulateCommand("AT+CEXTERNTONE=0");
        SimulateCommand("AT+ECHO?");
        SimulateCommand("AT+CMGF=1");
        SimulateCommand("AT+CSCS=\"IRA\"");
        SimulateCommand("AT+CREG?");
        SimulateCommand("AT+CGREG?");

        _isInitialized = true;
        _logger.LogInformation("Simulated test bench initialization completed successfully");
    }

    public void FourGModuleInitialization()
    {
        _logger.LogInformation("Starting simulated 4G module initialization");

        if (_options.SimulateFailures && _random.Next(100) < _options.FailurePercentage)
        {
            _logger.LogError("Simulated 4G module initialization failure");
            throw new InvalidOperationException("Simulated 4G module initialization failure");
        }

        Thread.Sleep(_options.CommandDelayMs);

        SimulateCommand("ATE0");
        SimulateCommand("AT+CPIN?");
        SimulateCommand("AT#ADSPC=1,0");
        SimulateCommand("AT#DVICFG=0,1,2,1,2");
        SimulateCommand("AT#DVICFG=1,1,2,1,2");
        SimulateCommand("AT#DVI=1,2,1");
        SimulateCommand("AT#DVICLK=512,0");
        SimulateCommand("AT#I2CWR=3,2,30,4,19");
        SimulateCommand("00101000240140002077404010502A0A400007");
        SimulateCommand("AT#I2CWR=3,2,30,17,1");
        SimulateCommand("8A");
        SimulateCommand("AT+CVHU=0");
        SimulateCommand("AT#DIALMODE=2");
        SimulateCommand("AT+CSQ");
        SimulateCommand("AT+CREG?");
        SimulateCommand("AT+CEREG?");

        _isInitialized = true;
        _logger.LogInformation("Simulated 4G module initialization completed successfully");
    }

    public void LaunchTest(string phoneNumber, bool audioOk)
    {
        if (!_isInitialized)
        {
            _logger.LogWarning("Attempting to launch test without initialization. Auto-initializing...");
            TestBenchInitialization();
        }

        _logger.LogInformation(
            "Launching simulated test call to {PhoneNumber} (audioOk={AudioOk})",
            phoneNumber,
            audioOk);

        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new ArgumentException("Phone number cannot be empty", nameof(phoneNumber));
        }

        if (_isInCall)
        {
            _logger.LogWarning("Already in a call. Hanging up previous call first.");
            HangUpCall();
        }

        // Simulate dialing
        SimulateCommand(BuildVoiceDialCommand(phoneNumber));
        _currentPhoneNumber = phoneNumber;
        _isInCall = true;

        // Simulate call connection delay
        if (_options.CallConnectDelayMs > 0)
        {
            _logger.LogDebug("Simulating call connection delay of {DelayMs}ms", _options.CallConnectDelayMs);
            Thread.Sleep(_options.CallConnectDelayMs);
        }

        // Simulate call connection failure
        if (_options.SimulateFailures && _random.Next(100) < _options.CallFailurePercentage)
        {
            _logger.LogWarning("Simulated call connection failure (BUSY/NO CARRIER)");
            _isInCall = false;
            _currentPhoneNumber = null;
            throw new InvalidOperationException("Simulated call failure: NO CARRIER");
        }

        _callStartTime = DateTime.UtcNow;
        _logger.LogInformation("Simulated call connected to {PhoneNumber}", phoneNumber);

        if (audioOk)
        {
            // Simulate DTMF detection
            SimulateCommand("AT+DDET=1");
            SimulateCommand("AT+VTS=1");
            SimulateCommand("AT+DDET=0");
            _logger.LogDebug("Simulated DTMF tone sent");
        }
    }

    public void HangUpCall()
    {
        if (!_isInCall)
        {
            _logger.LogDebug("No active call to hang up");
            return;
        }

        var callDuration = _callStartTime.HasValue
            ? (DateTime.UtcNow - _callStartTime.Value).TotalSeconds
            : 0;

        _logger.LogInformation(
            "Hanging up simulated call to {PhoneNumber} (duration: {DurationSeconds:F1}s)",
            _currentPhoneNumber,
            callDuration);

        SimulateCommand("+++");
        SimulateCommand("ATH");

        _isInCall = false;
        _currentPhoneNumber = null;
        _callStartTime = null;
    }

    public bool IsPortOpen()
    {
        // Simulator is always "open" once initialized
        var isOpen = _isInitialized;
        _logger.LogDebug("IsPortOpen check: {IsOpen}", isOpen);
        return isOpen;
    }

    public bool PingModem()
    {
        _logger.LogDebug("Pinging simulated modem");

        if (_options.SimulateFailures && _random.Next(100) < _options.PingFailurePercentage)
        {
            _logger.LogWarning("Simulated ping failure");
            return false;
        }

        SimulateCommand("AT");
        _logger.LogInformation("Simulated modem responded to ping");
        return true;
    }

    private void SimulateCommand(string command)
    {
        _logger.LogTrace("Simulated TX -> {Command}", command.Replace("\r", "\\r"));

        // Small delay to simulate serial communication
        if (_options.CommandDelayMs > 0)
        {
            Thread.Sleep(_options.CommandDelayMs);
        }

        _logger.LogTrace("Simulated RX <- OK");
    }

    private static string BuildVoiceDialCommand(string phoneNumber)
    {
        var normalizedPhoneNumber = phoneNumber.Trim().TrimEnd(';');
        return $"ATDT{normalizedPhoneNumber};";
    }

    public void Dispose()
    {
        if (_isInCall)
        {
            _logger.LogWarning("Disposing simulator while in call. Auto-hanging up.");
            HangUpCall();
        }

        _logger.LogInformation("GSM Simulator disposed");
    }
}

/// <summary>
/// Configuration options for the GSM simulator
/// </summary>
public sealed class GsmSimulatorOptions
{
    /// <summary>
    /// Delay in milliseconds to simulate AT command processing time
    /// </summary>
    public int CommandDelayMs { get; set; } = 50;

    /// <summary>
    /// Delay in milliseconds to simulate call connection time
    /// </summary>
    public int CallConnectDelayMs { get; set; } = 2000;

    /// <summary>
    /// Enable random failures to test error handling
    /// </summary>
    public bool SimulateFailures { get; set; } = false;

    /// <summary>
    /// Percentage chance (0-100) of initialization failure when SimulateFailures is true
    /// </summary>
    public int FailurePercentage { get; set; } = 10;

    /// <summary>
    /// Percentage chance (0-100) of call connection failure when SimulateFailures is true
    /// </summary>
    public int CallFailurePercentage { get; set; } = 15;

    /// <summary>
    /// Percentage chance (0-100) of ping failure when SimulateFailures is true
    /// </summary>
    public int PingFailurePercentage { get; set; } = 5;
}
