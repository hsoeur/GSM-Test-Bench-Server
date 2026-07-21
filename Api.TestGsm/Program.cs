using Api.TestGsm.Configuration;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);
var appOptions = builder.Configuration.GetSection("App").Get<AppOptions>() ?? new AppOptions();
var serilogOptions = builder.Configuration.GetSection("SerilogFile").Get<SerilogFileOptions>() ?? new SerilogFileOptions();

string logsRoot = Path.Combine(AppContext.BaseDirectory, RequireConfig(serilogOptions.LogsDirectoryName, "SerilogFile:LogsDirectoryName"));
string dayFolderName = DateTime.UtcNow.ToString(RequireConfig(serilogOptions.DayFolderFormat, "SerilogFile:DayFolderFormat"));
string dayFolder = Path.Combine(logsRoot, dayFolderName);
Directory.CreateDirectory(dayFolder);

int restartNumber = 1;
var logFileNameFormat = RequireConfig(serilogOptions.FileNameFormat, "SerilogFile:FileNameFormat");
var existing = Directory.EnumerateFiles(dayFolder, string.Format(logFileNameFormat, dayFolderName, "*"))
    .Select(p => Path.GetFileNameWithoutExtension(p))
    .Select(name =>
    {
        var parts = name.Split('-');
        if (parts.Length >= 5 && int.TryParse(parts[4], out var n)) return n;
        return 0;
    })
    .Where(n => n > 0)
    .DefaultIfEmpty(0)
    .Max();
restartNumber = existing + 1;

string logFilePath = Path.Combine(dayFolder, string.Format(logFileNameFormat, dayFolderName, restartNumber));
var frameworkMinimumLevel = Enum.Parse<LogEventLevel>(
    RequireConfig(serilogOptions.FrameworkMinimumLevel, "SerilogFile:FrameworkMinimumLevel"),
    ignoreCase: true);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", frameworkMinimumLevel)
    .MinimumLevel.Override("Microsoft.AspNetCore", frameworkMinimumLevel)
    .MinimumLevel.Override("System", frameworkMinimumLevel)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("RestartNumber", restartNumber)
    .WriteTo.Console(outputTemplate:
        RequireConfig(serilogOptions.ConsoleOutputTemplate, "SerilogFile:ConsoleOutputTemplate"))
    .WriteTo.File(
        path: logFilePath,
        restrictedToMinimumLevel: LogEventLevel.Debug,
        rollingInterval: RollingInterval.Infinite,
        shared: true,
        flushToDiskInterval: TimeSpan.FromSeconds(serilogOptions.FlushToDiskIntervalSeconds),
        outputTemplate:
            RequireConfig(serilogOptions.FileOutputTemplate, "SerilogFile:FileOutputTemplate"))
    .CreateLogger();

builder.Host.UseSerilog();

// Bind GSM options from configuration
builder.Services.Configure<Api.TestGsm.Services.GsmOptions>(builder.Configuration.GetSection("Gsm"));
builder.Services.Configure<Api.TestGsm.Services.GsmSimulatorOptions>(builder.Configuration.GetSection("GsmSimulator"));
builder.Services.Configure<ApiResponseOptions>(builder.Configuration.GetSection("ApiResponses"));

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy(RequireConfig(appOptions.CorsPolicyName, "App:CorsPolicyName"), policy =>
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader());
});

// Register GSM service (real or simulator based on configuration)
var useSimulator = builder.Configuration.GetValue<bool>("UseSimulator", false);
if (useSimulator)
{
    Log.Information("Using GSM Simulator (no physical hardware required)");
    builder.Services.AddSingleton<Api.TestGsm.Services.IGsmService, Api.TestGsm.Services.GsmSimulatorService>();
}
else
{
    Log.Information("Using Real GSM Hardware via Serial Port");
    builder.Services.AddSingleton<Api.TestGsm.Services.IGsmService, Api.TestGsm.Services.GsmService>();
}

var app = builder.Build();

if (appOptions.EnableSwagger)
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.RoutePrefix = RequireConfig(appOptions.SwaggerRoutePrefix, "App:SwaggerRoutePrefix");
    });
}

app.MapGet("/", () => Results.Redirect(RequireConfig(appOptions.RootRedirectPath, "App:RootRedirectPath")));

// If you only use HTTP, keep HTTPS redirection disabled
// app.UseHttpsRedirection();

// Apply CORS globally
app.UseCors(RequireConfig(appOptions.CorsPolicyName, "App:CorsPolicyName"));

app.UseAuthorization();

app.MapControllers();

try
{
    Log.Information("Starting Api.TestGsm. Log file: {LogFile}", logFilePath);
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

static string RequireConfig(string? value, string key)
    => string.IsNullOrWhiteSpace(value)
        ? throw new InvalidOperationException($"Missing configuration value '{key}'.")
        : value;
