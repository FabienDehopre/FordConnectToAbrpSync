# AGENTS.md

Instructions for coding agents working in this repository.

## Agent skills

### Issue tracker

Issues are tracked as GitHub issues via the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

Canonical triage vocabulary (`needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`). See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: `CONTEXT.md` + `docs/adr/` at the repo root. See `docs/agents/domain.md`.

## Build & test

.NET 10 (`global.json` pins the SDK). Tests use TUnit on the Microsoft.Testing.Platform runner.

```bash
dotnet build                                              # build solution
dotnet run --project FordConnectToAbrpSync                # run the sync worker
dotnet run --project FordConnectToAbrpSync -- login       # interactive Ford login (needs browser + loopback)
dotnet run --project FordConnectToAbrpSync -- test        # dump one raw telemetry snapshot
dotnet run --project FordConnectToAbrpSync.Tests          # run all tests
dotnet run --project FordConnectToAbrpSync.Tests -- --treenode-filter "/*/*/SyncDeciderTests/*"  # single class
```

## Architecture

Native AOT worker (`PublishAot=true`). Two rules govern edits:

- **No reflection-based serialization.** All JSON goes through `Serialization/AppJsonSerializerContext.cs` (source-generated). Serilog is configured in code, not `ReadFrom.Configuration`. See ADR 0003, 0005.
- **AOT-safe config binding** via `EnableConfigurationBindingGenerator`; only scalars are read ad hoc from `IConfiguration`.

`Program.cs` is the composition root: dispatches on `args[0]` to Run (default, `SyncWorker` hosted service) / `login` / `test`, and wires DI. Flow of a **Sync Cycle** (`Sync/SyncWorker.cs`):

1. `Ford/FordTokenService` supplies an access token (refreshed from the encrypted refresh token in `Security/EncryptedFileTokenStore`, protected via Data Protection).
2. `Ford/FordTelemetryClient` fetches a Telemetry Snapshot. Its `HttpClient` stacks resilience (outer) then `FordAuthenticationHandler` (inner, injects bearer); the token client has resilience only, to avoid auth recursion. See ADR 0004.
3. `Sync/SyncDecider` short-circuits on Stale Snapshots and on unchanged Watched Subsets (`WatchedSubset` + `WatchedSubsetComparer`, tolerances in `ChangeTolerances`). See ADR 0002.
4. `Sync/AbrpTelemetryMapper` maps to the ABRP payload; `Abrp/AbrpClient` relays.

Both HTTP clients are hand-written slim clients (no generated SDK) for AOT. `Configuration/*Options.cs` bind `Sync`/`Ford`/`Abrp` config sections.
