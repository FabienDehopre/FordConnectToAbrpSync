using System.Text.Json.Serialization;
using FordConnectToAbrpSync.Abrp;
using FordConnectToAbrpSync.Ford;
using FordConnectToAbrpSync.Security;

namespace FordConnectToAbrpSync.Serialization;

/// <summary>
/// Source-generated JSON metadata for all wire types. Required for Native AOT —
/// no reflection-based (de)serialization anywhere in the app.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(FordTelemetryResponse))]
[JsonSerializable(typeof(AbrpTelemetry))]
[JsonSerializable(typeof(AbrpSendResponse))]
[JsonSerializable(typeof(StoredToken))]
[JsonSerializable(typeof(FordTokenResponse))]
internal sealed partial class AppJsonSerializerContext : JsonSerializerContext;
