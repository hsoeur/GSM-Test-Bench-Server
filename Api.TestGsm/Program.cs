using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// Listen on port5001 on all interfaces
builder.WebHost.UseUrls("http://0.0.0.0:5001");

// Create daily logs directory and incremented filename per app start
string logsRoot = Path.Combine(AppContext.BaseDirectory, "logs");
string dayFolderName = DateTime.UtcNow.ToString("yyyy-MM-dd");
string dayFolder = Path.Combine(logsRoot, dayFolderName);
Directory.CreateDirectory(dayFolder);

int restartNumber = 1;
var existing = Directory.EnumerateFiles(dayFolder, $"log-{dayFolderName}-*.log")
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

string logFilePath = Path.Combine(dayFolder, $"log-{dayFolderName}-{restartNumber}.log");

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    // Reduce noise from framework sources
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("RestartNumber", restartNumber)
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: logFilePath,
        restrictedToMinimumLevel: LogEventLevel.Debug,
        rollingInterval: RollingInterval.Infinite,
        shared: true,
        flushToDiskInterval: TimeSpan.FromSeconds(1),
        outputTemplate:
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

builder.Host.UseSerilog();

// Bind GSM options from configuration
builder.Services.Configure<Api.TestGsm.Services.GsmOptions>(builder.Configuration.GetSection("Gsm"));
builder.Services.Configure<Api.TestGsm.Services.GsmSimulatorOptions>(builder.Configuration.GetSection("GsmSimulator"));

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// CORS: allow any origin, method, and header (adjust for production as needed)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
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

// Always enable Swagger and serve UI under /swagger
app.UseSwagger();
app.UseSwaggerUI(); // RoutePrefix defaults to "swagger"

// Optional: redirect root to Swagger UI
app.MapGet("/", () => Results.Redirect("/swagger"));

// If you only use HTTP, keep HTTPS redirection disabled
// app.UseHttpsRedirection();

// Apply CORS globally
app.UseCors("AllowAll");

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