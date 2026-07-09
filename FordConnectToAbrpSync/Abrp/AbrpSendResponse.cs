namespace FordConnectToAbrpSync.Abrp;

/// <summary>Minimal shape of the ABRP /tlm/send response.</summary>
internal sealed record AbrpSendResponse
{
    public string? Status { get; init; }

    public string? Error { get; init; }
}
