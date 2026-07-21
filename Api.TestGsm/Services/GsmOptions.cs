namespace Api.TestGsm.Services;

public sealed class GsmOptions
{
    public string? PortName { get; set; }
    public string? WindowsDefaultPortName { get; set; }
    public string? LinuxDefaultPortName { get; set; }
    public int? BaudRate { get; set; }
    public int? DataBits { get; set; }
    public string? StopBits { get; set; }
    public string? Parity { get; set; }
    public string? Handshake { get; set; }
    public string? NewLine { get; set; }
    public int? ReadTimeout { get; set; }
    public int? WriteTimeout { get; set; }
    public int? OpenDelayMs { get; set; }
    public int? ReadPollingDelayMs { get; set; }
    public int? WaitForTextPollingDelayMs { get; set; }
    public int? ExecGsmCommandSlackLines { get; set; }
    public int? ExecAndReadLineTimeoutMs { get; set; }
    public int? ExecAndReadMaxIterations { get; set; }
    public int? CallStatusMaxAttempts { get; set; }
    public int? CallStatusPollingDelayMs { get; set; }
    public GsmProtocolOptions Protocol { get; set; } = new();
    public GsmTimeoutOptions Timeouts { get; set; } = new();
    public GsmCommandSequences CommandSequences { get; set; } = new();
}

public sealed class GsmProtocolOptions
{
    public string? VoiceDialCommandFormat { get; set; }
    public string? I2cPrompt { get; set; }
    public string? OkResponse { get; set; }
    public string? ErrorResponse { get; set; }
    public string? ClccPrefix { get; set; }
    public string? NoCarrierText { get; set; }
    public string? BusyText { get; set; }
    public string? NoAnswerText { get; set; }
}

public sealed class GsmTimeoutOptions
{
    public int TwoSecondsMs { get; set; } = 2000;
    public int FiveSecondsMs { get; set; } = 5000;
    public int TenSecondsMs { get; set; } = 10000;
    public int FifteenSecondsMs { get; set; } = 15000;
    public int TwentyFiveSecondsMs { get; set; } = 25000;
}

public sealed class GsmCommandSequences
{
    public List<GsmCommandStep> TestBenchInitialization { get; set; } = [];
    public List<GsmCommandStep> TestBenchInitializationFailureCleanup { get; set; } = [];
    public List<GsmCommandStep> FourGModuleInitialization { get; set; } = [];
    public List<GsmCommandStep> AudioMarker { get; set; } = [];
    public List<GsmCommandStep> AudioMarkerFailureCleanup { get; set; } = [];
    public List<GsmCommandStep> TestCallFailureCleanup { get; set; } = [];
    public List<GsmCommandStep> HangUp { get; set; } = [];
    public List<GsmCommandStep> Ping { get; set; } = [];
    public List<GsmCommandStep> WaitForVoiceConnectedStatus { get; set; } = [];
    public string[] TestCallConnectedResponses { get; set; } = [];
}

public sealed class GsmCommandStep
{
    public string? Command { get; set; }
    public string? Payload { get; set; }
    public string? RequiredText { get; set; }
    public string[] ExpectedResponses { get; set; } = [];
    public int? TimeoutMs { get; set; }
    public string? TimeoutKey { get; set; }
    public bool ContinueOnFailure { get; set; }
}
