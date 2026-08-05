namespace FordConnectToAbrpSync.Health;

internal sealed record HealthResponse(string Status, bool SyncWorkerAlive);
