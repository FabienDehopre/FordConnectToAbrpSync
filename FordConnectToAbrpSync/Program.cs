using FordConnectToAbrpSync.Abrp;
using FordConnectToAbrpSync.Configuration;
using FordConnectToAbrpSync.Ford;
using FordConnectToAbrpSync.Security;
using FordConnectToAbrpSync.Sync;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;

// Shared output template for every sink (console + file). Structured properties
// are rendered as JSON so they stay visible without a structured backend.
const string OutputTemplate =
    "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {SourceContext}: {Message:lj} {Properties:j}{NewLine}{Exception}";

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
    var builder = Host.CreateApplicationBuilder(args);

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
            lc.WriteTo.File(
                path: "./logs/sync-.log",
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
        builder.Services.AddHostedService<SyncWorker>();
    }

    var host = builder.Build();

    if (isLogin)
    {
        var login = host.Services.GetRequiredService<LoginCommand>();
        return await login.RunAsync(CancellationToken.None);
    }

    if (isTest)
    {
        var test = host.Services.GetRequiredService<TestCommand>();
        return await test.RunAsync(CancellationToken.None);
    }

    host.Run();
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
// Falls back to Information when absent or unparseable.
static LogEventLevel ReadMinimumLevel(IConfiguration configuration)
{
    var raw = configuration["Serilog:MinimumLevel:Default"];
    return Enum.TryParse<LogEventLevel>(raw, ignoreCase: true, out var level)
        ? level
        : LogEventLevel.Information;
}
