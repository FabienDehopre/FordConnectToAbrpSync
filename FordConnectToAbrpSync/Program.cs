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

var builder = Host.CreateApplicationBuilder(args);

var isLogin = args.Length > 0 && string.Equals(args[0], "login", StringComparison.OrdinalIgnoreCase);

// --- Options ---------------------------------------------------------------
builder.Services.Configure<SyncOptions>(builder.Configuration.GetSection(SyncOptions.SectionName));
builder.Services.Configure<FordOptions>(builder.Configuration.GetSection(FordOptions.SectionName));
builder.Services.Configure<AbrpOptions>(builder.Configuration.GetSection(AbrpOptions.SectionName));

var fordOptions = builder.Configuration.GetSection(FordOptions.SectionName).Get<FordOptions>() ?? new FordOptions();

// --- Token Store (encrypted at rest via Data Protection) -------------------
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(fordOptions.KeysDirectory))
    .SetApplicationName("FordConnectToAbrpSync");

builder.Services.AddSingleton<ITokenStore>(sp => new EncryptedFileTokenStore(
    sp.GetRequiredService<IOptions<FordOptions>>().Value.TokenFilePath,
    sp.GetRequiredService<IDataProtectionProvider>()));

// --- Change detection ------------------------------------------------------
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<SyncOptions>>().Value.Tolerances);
builder.Services.AddSingleton<SyncDecider>();

// --- Ford auth -------------------------------------------------------------
builder.Services.AddSingleton<FordTokenService>();
builder.Services.AddTransient<FordAuthenticationHandler>();
builder.Services.AddTransient<LoginCommand>();

// Token endpoint client: resilience only, NO bearer handler (avoids recursion).
builder.Services.AddHttpClient<FordAuthClient>()
    .AddStandardResilienceHandler(builder.Configuration.GetSection("Resilience:Ford"));

// Telemetry client: resilience (outer) then bearer auth handler (inner).
var fordTelemetryBuilder = builder.Services.AddHttpClient<FordTelemetryClient>(client =>
    client.BaseAddress = new Uri(fordOptions.BaseUrl));
fordTelemetryBuilder.AddStandardResilienceHandler(builder.Configuration.GetSection("Resilience:Ford"));
fordTelemetryBuilder.AddHttpMessageHandler<FordAuthenticationHandler>();

// --- ABRP client -----------------------------------------------------------
var abrpBaseUrl = builder.Configuration.GetSection(AbrpOptions.SectionName).Get<AbrpOptions>()?.BaseUrl
                  ?? new AbrpOptions().BaseUrl;
builder.Services.AddHttpClient<AbrpClient>(client => client.BaseAddress = new Uri(abrpBaseUrl))
    .AddStandardResilienceHandler(builder.Configuration.GetSection("Resilience:Abrp"));

// --- Run vs Login ----------------------------------------------------------
if (!isLogin)
{
    builder.Services.AddHostedService<SyncWorker>();
}

var host = builder.Build();

if (isLogin)
{
    var login = host.Services.GetRequiredService<LoginCommand>();
    return await login.RunAsync(CancellationToken.None);
}

host.Run();
return 0;
