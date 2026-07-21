namespace Api.TestGsm.Configuration;

public sealed class AppOptions
{
    public string? CorsPolicyName { get; set; }
    public bool EnableSwagger { get; set; }
    public string? SwaggerRoutePrefix { get; set; }
    public string? RootRedirectPath { get; set; }
}

public sealed class ApiResponseOptions
{
    public string? Init4GSuccessMessage { get; set; }
    public string? ModemTimeoutError { get; set; }
    public string? InitializationFailedError { get; set; }
    public string? PhoneNumberRequiredMessage { get; set; }
    public string? TimeoutDetectionText { get; set; }
}

public sealed class SerilogFileOptions
{
    public string? LogsDirectoryName { get; set; }
    public string? DayFolderFormat { get; set; }
    public string? FileNameFormat { get; set; }
    public string? FrameworkMinimumLevel { get; set; }
    public string? ConsoleOutputTemplate { get; set; }
    public string? FileOutputTemplate { get; set; }
    public int FlushToDiskIntervalSeconds { get; set; }
}
