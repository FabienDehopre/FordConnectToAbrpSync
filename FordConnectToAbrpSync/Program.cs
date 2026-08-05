using FordConnectToAbrpSync.Abrp;
using FordConnectToAbrpSync.Configuration;
using FordConnectToAbrpSync.Ford;
using FordConnectToAbrpSync.Health;
using FordConnectToAbrpSync.Security;
using FordConnectToAbrpSync.Sync;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;

// Shared output template for every sink (console + file). Structured properties
// are rendered as JSON so they stay visible without a structured backend.
const string OutputTemplate =
    "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {SourceContext}: {Message:lj} {Properties:j}{NewLine}{Exception}";

// Directory for the rolling file sink (Run mode). Gitignored and absent on a
// fresh checkout outside Docker, so it is created before the sink is wired.
const string LogDirectory = "./logs";

// Where the Sync Cycle loop's liveness deadline is recorded (Run mode).
// Relative to WORKDIR, like LogDirectory — resolves to /app/heartbeat under
// Docker for both ENTRYPOINT and HEALTHCHECK. A bare filename, not a
// subdirectory: "./health/..." would collide case-insensitively with the
// Health/ source folder on a local macOS checkout. Not on the /data volume:
// it carries no state worth persisting across restarts.
const string HeartbeatFilePath = "./heartbeat";

// The healthcheck subcommand short-circuits before the host is built: no
// Serilog, no DataProtection key ring, no HttpClients. That keeps it fast for
// something Docker invokes every interval, and — more importantly — keeps it
// from fighting the live worker over the file log sink (shared: false) and
// the key ring, which it would touch if it went through isRun below.
if (args.Length > 0 && string.Equals(args[0], "healthcheck", StringComparison.OrdinalIgnoreCase))
{
    return HeartbeatCheck.Run(HeartbeatFilePath, DateTimeOffset.UtcNow) ? 0 : 1;
}

// Serilog is configured entirely in code (no ReadFrom.Configuration): the
// reflection-based settings reader is RequiresDynamicCode/RequiresUnreferencedCode
// and is incompatible with this project's Native AOT build. See ADR 0005.

// Stage 1: bootstrap logger — captures failures before the host is built.
// Everything goes to stderr so stdout stays clean for the test/login commands.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(
        standardErrorFromLevel: LogEventLevel.Verbose,
        outputTemplate: OutputTemplate)
    .CreateBootstrapLogger();

try
{
    // Slim web builder instead of a plain host: Run mode serves the Wake/Sleep
    // Signal endpoints on Kestrel (see ADR 0007). Login/test/healthcheck never
    // start the server — Kestrel only spins up when app.Run() is reached.
    var builder = WebApplication.CreateSlimBuilder(args);

    var isLogin = args.Length > 0 && string.Equals(args[0], "login", StringComparison.OrdinalIgnoreCase);
    var isTest = args.Length > 0 && string.Equals(args[0], "test", StringComparison.OrdinalIgnoreCase);

    // The file sink only runs for the headless worker (Run). Login and test are
    // short, interactive commands whose operator is watching stderr live.
    var isRun = !isLogin && !isTest;

    // Overall minimum level is the one knob read from configuration (a scalar is
    // AOT-safe); namespace overrides are constants and stay in code.
    var minimumLevel = ReadMinimumLevel(builder.Configuration);

    // Stage 2: full logger with DI services.
    builder.Services.AddSerilog((services, lc) =>
    {
        lc.MinimumLevel.Is(minimumLevel)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .ReadFrom.Services(services)
            .WriteTo.Console(
                standardErrorFromLevel: LogEventLevel.Verbose,
                outputTemplate: OutputTemplate);

        if (isRun)
        {
            Directory.CreateDirectory(LogDirectory);
            lc.WriteTo.File(
                path: $"{LogDirectory}/sync-.log",
                outputTemplate: OutputTemplate,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 31,
                fileSizeLimitBytes: 104_857_600,
                rollOnFileSizeLimit: true,
                shared: false);
        }
    });

    // --- Options -----------------------------------------------------------
    builder.Services.Configure<SyncOptions>(builder.Configuration.GetSection(SyncOptions.SectionName));
    builder.Services.Configure<FordOptions>(builder.Configuration.GetSection(FordOptions.SectionName));
    builder.Services.Configure<AbrpOptions>(builder.Configuration.GetSection(AbrpOptions.SectionName));
    builder.Services.Configure<SignalOptions>(builder.Configuration.GetSection(SignalOptions.SectionName));

    var fordOptions = builder.Configuration.GetSection(FordOptions.SectionName).Get<FordOptions>() ?? new FordOptions();

    // --- Token Store (encrypted at rest via Data Protection) ---------------
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(fordOptions.KeysDirectory))
        .SetApplicationName("FordConnectToAbrpSync");

    builder.Services.AddSingleton<ITokenStore>(sp => new EncryptedFileTokenStore(
        sp.GetRequiredService<IOptions<FordOptions>>().Value.TokenFilePath,
        sp.GetRequiredService<IDataProtectionProvider>()));

    // --- Change detection --------------------------------------------------
    builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<SyncOptions>>().Value.Tolerances);
    builder.Services.AddSingleton<SyncDecider>();
    builder.Services.AddSingleton<SyncPacer>();

    // --- Ford auth ---------------------------------------------------------
    builder.Services.AddSingleton<FordTokenService>();
    builder.Services.AddTransient<FordAuthenticationHandler>();
    builder.Services.AddTransient<LoginCommand>();
    builder.Services.AddTransient<TestCommand>();

    // Token endpoint client: resilience only, NO bearer handler (avoids recursion).
    builder.Services.AddHttpClient<FordAuthClient>()
        .AddStandardResilienceHandler(builder.Configuration.GetSection("Resilience:Ford"));

    // Telemetry client: resilience (outer) then bearer auth handler (inner).
    var fordTelemetryBuilder = builder.Services.AddHttpClient<FordTelemetryClient>(client =>
        client.BaseAddress = new Uri(fordOptions.BaseUrl));
    fordTelemetryBuilder.AddStandardResilienceHandler(builder.Configuration.GetSection("Resilience:Ford"));
    fordTelemetryBuilder.AddHttpMessageHandler<FordAuthenticationHandler>();

    // --- ABRP client -------------------------------------------------------
    var abrpBaseUrl = builder.Configuration.GetSection(AbrpOptions.SectionName).Get<AbrpOptions>()?.BaseUrl
                      ?? new AbrpOptions().BaseUrl;
    builder.Services.AddHttpClient<AbrpClient>(client => client.BaseAddress = new Uri(abrpBaseUrl))
        .AddStandardResilienceHandler(builder.Configuration.GetSection("Resilience:Abrp"));

    // --- Run vs Login vs Test ----------------------------------------------
    if (isRun)
    {
        builder.Services.AddSingleton(new HeartbeatWriter(HeartbeatFilePath));
        builder.Services.AddHostedService<SyncWorker>();
    }

    using var app = builder.Build();

    if (isLogin)
    {
        var login = app.Services.GetRequiredService<LoginCommand>();
        return await login.RunAsync(CancellationToken.None);
    }

    if (isTest)
    {
        var test = app.Services.GetRequiredService<TestCommand>();
        return await test.RunAsync(CancellationToken.None);
    }

    // --- Health endpoint (Run mode only) ------------------------------------
    // Unauthenticated, unlike Wake/Sleep: just confirms the container is up
    // when hit manually (e.g. through the Cloudflare tunnel or docker exec curl).
    app.MapGet("/healthz", () => Results.Ok());

    // --- Wake/Sleep Signal endpoints (Run mode only) -----------------------
    // Reachable from the driver's phone via a Cloudflare tunnel. Auth is a
    // bearer secret checked in-app so the endpoints stay closed even if the
    // tunnel config drifts. Handlers stay thin: all pacing behavior lives in
    // SyncPacer, which is what the tests cover (Program.cs is excluded).
    if (app.Services.GetRequiredService<IOptions<SignalOptions>>().Value.Secret is null or "")
    {
        app.Logger.LogWarning(
            "Signal:Secret is not configured; the /wake and /sleep endpoints will refuse every request.");
    }

    app.MapPost("/wake", (
        HttpRequest request,
        SyncPacer pacer,
        IOptions<SignalOptions> signal,
        IOptionsMonitor<SyncOptions> sync) =>
    {
        if (!SignalAuthenticator.IsAuthorized(signal.Value.Secret, request.Headers.Authorization))
        {
            return Results.Unauthorized();
        }

        pacer.Wake(DateTimeOffset.UtcNow, sync.CurrentValue.BoostWindow);
        app.Logger.LogInformation("Wake Signal received; Boost Window opened.");
        return Results.NoContent();
    });

    app.MapPost("/sleep", (
        HttpRequest request,
        SyncPacer pacer,
        IOptions<SignalOptions> signal) =>
    {
        if (!SignalAuthenticator.IsAuthorized(signal.Value.Secret, request.Headers.Authorization))
        {
            return Results.Unauthorized();
        }

        pacer.Sleep();
        app.Logger.LogInformation("Sleep Signal received; Boost Window closed.");
        return Results.NoContent();
    });

    app.Run();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

// Reads Serilog:MinimumLevel:Default as a scalar and maps it to a LogEventLevel.
// Falls back to Information when absent or unparseable; a present-but-invalid
// value is surfaced via the bootstrap logger so the mistake is visible at startup.
static LogEventLevel ReadMinimumLevel(IConfiguration configuration)
{
    var raw = configuration["Serilog:MinimumLevel:Default"];
    if (Enum.TryParse<LogEventLevel>(raw, ignoreCase: true, out var level))
    {
        return level;
    }

    if (!string.IsNullOrWhiteSpace(raw))
    {
        Log.Warning(
            "Invalid Serilog:MinimumLevel:Default {RawValue}; falling back to {FallbackLevel}",
            raw,
            LogEventLevel.Information);
    }

    return LogEventLevel.Information;
}
