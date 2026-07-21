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
    private readonly GsmOptions _gsmOptions;
    private bool _isInitialized;
    private bool _isInCall;
    private string? _currentPhoneNumber;
    private DateTime? _callStartTime;
    private readonly Random _random = new();

    public GsmSimulatorService(
        IOptions<GsmSimulatorOptions> options,
        IOptions<GsmOptions> gsmOptions,
        ILogger<GsmSimulatorService> logger)
    {
        _logger = logger;
        _options = options.Value ?? new GsmSimulatorOptions();
        _gsmOptions = gsmOptions.Value ?? new GsmOptions();

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

        SimulateSteps(_gsmOptions.CommandSequences.TestBenchInitialization, "Gsm:CommandSequences:TestBenchInitialization");

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

        SimulateSteps(_gsmOptions.CommandSequences.FourGModuleInitialization, "Gsm:CommandSequences:FourGModuleInitialization");

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
            _logger.LogWarning("Simulated call connection failure");
            _isInCall = false;
            _currentPhoneNumber = null;
            throw new InvalidOperationException(RequiredValue(_options.CallFailureMessage, "GsmSimulator:CallFailureMessage"));
        }

        _callStartTime = DateTime.UtcNow;
        _logger.LogInformation("Simulated call connected to {PhoneNumber}", phoneNumber);

        if (audioOk)
        {
            SimulateSteps(_gsmOptions.CommandSequences.AudioMarker, "Gsm:CommandSequences:AudioMarker");
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

        SimulateSteps(_gsmOptions.CommandSequences.HangUp, "Gsm:CommandSequences:HangUp");

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

        SimulateSteps(_gsmOptions.CommandSequences.Ping, "Gsm:CommandSequences:Ping");
        _logger.LogInformation("Simulated modem responded to ping");
        return true;
    }

    private void SimulateSteps(IReadOnlyCollection<GsmCommandStep> steps, string configurationKey)
    {
        if (steps.Count == 0)
        {
            throw new InvalidOperationException($"Missing GSM command sequence '{configurationKey}'.");
        }

        foreach (var step in steps)
        {
            if (!string.IsNullOrEmpty(step.Command))
            {
                SimulateCommand(step.Command);
            }

            if (!string.IsNullOrEmpty(step.Payload))
            {
                SimulateCommand(step.Payload);
            }
        }
    }

    private void SimulateCommand(string command)
    {
        _logger.LogTrace("Simulated TX -> {Command}", command.Replace("\r", "\\r"));

            
        if (_options.CommandDelayMs > 0)
        {
            Thread.Sleep(_options.CommandDelayMs);
        }

        _logger.LogTrace("Simulated RX <- OK");
    }

    private string BuildVoiceDialCommand(string phoneNumber)
    {
        var normalizedPhoneNumber = phoneNumber.Trim().TrimEnd(';');
        return string.Format(
            RequiredValue(_gsmOptions.Protocol.VoiceDialCommandFormat, "Gsm:Protocol:VoiceDialCommandFormat"),
            normalizedPhoneNumber);
    }

    private static string RequiredValue(string? value, string configurationKey)
        => string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Missing configuration value '{configurationKey}'.")
            : value;

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

    /// <summary>
    /// Exception message raised when a simulated call cannot connect.
    /// </summary>
    public string? CallFailureMessage { get; set; }
}
