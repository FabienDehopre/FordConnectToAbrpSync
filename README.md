# FordConnectToAbrpSync

A .NET 10 worker that polls a Ford vehicle's telemetry and relays the
route-relevant parts to [A Better Route Planner (ABRP)](https://abetterrouteplanner.com)
for live journey tracking. It only sends to ABRP when the data meaningfully
changes, and both HTTP clients are resilient to rate limiting and transient
errors.

See [`CONTEXT.md`](./CONTEXT.md) for the domain glossary and
[`docs/adr/`](./docs/adr) for the key design decisions.

## How it works

Each **Sync Cycle** (default every 60 s, configurable and hot-reloadable):

1. Acquire a Ford access token (refreshed from a stored refresh token).
2. `GET /v1/telemetry` — a **Telemetry Snapshot**.
3. Short-circuit if the snapshot's report time hasn't advanced (**Stale Snapshot**).
4. Map the snapshot to the ABRP payload.
5. Relay to ABRP only if the **Watched Subset** (soc, power, speed, lat, lon,
   charging, DC-fast-charging, parked) changed beyond noise tolerances since the
   last successful relay.

## Configuration

Non-secret settings live in [`appsettings.json`](./FordConnectToAbrpSync/appsettings.json).
Secrets must come from user-secrets (dev) or environment variables (prod):

| Setting | Env var | Meaning |
| --- | --- | --- |
| `Ford:ClientId` | `Ford__ClientId` | Ford app-registration client id |
| `Ford:ClientSecret` | `Ford__ClientSecret` | Ford app-registration client secret |
| `Abrp:ApiKey` | `Abrp__ApiKey` | ABRP partner API key |
| `Abrp:Token` | `Abrp__Token` | ABRP per-vehicle user token |

Useful non-secret overrides: `Sync:Interval` (e.g. `00:00:30`),
`Sync:InvertPowerSign` (flip if charge/discharge power sign is reversed),
`Ford:ApplicationId` (if your Ford app requires the `Application-Id` header).

### Ford app registration

Create a Ford app registration whose redirect URI is `http://localhost`
(the loopback port is chosen at runtime). Authorize it against your vehicle in
the FordPass portal. The requested scope must include `offline_access` so Ford
issues a refresh token.

## Running

### 1. Log in (once)

Interactive — needs a browser and a loopback redirect, so run it on your machine
(not headless inside the container):

```bash
# dev
dotnet user-secrets set "Ford:ClientId" "..." --project FordConnectToAbrpSync
dotnet user-secrets set "Ford:ClientSecret" "..." --project FordConnectToAbrpSync
dotnet run --project FordConnectToAbrpSync -- login
```

This opens the Ford authorize page, catches the redirect, and writes the
encrypted refresh token to `./data/ford-token.json` (+ a Data Protection key
ring in `./data/keys`).

### 2. Run the sync

```bash
dotnet run --project FordConnectToAbrpSync
```

### Docker

```bash
cp .env.example .env      # fill in the four secrets
# Log in once on the host to populate ./data (same volume the container mounts):
dotnet run --project FordConnectToAbrpSync -- login
docker compose up -d --build
```

The container mounts `./data` for the token + key ring and reads secrets from
the environment. Because the `login` flow needs an interactive browser and a
loopback redirect, run it on the host as shown; the container only ever runs the
headless sync.

## Tests

```bash
dotnet run --project FordConnectToAbrpSync.Tests
```

(TUnit uses Microsoft.Testing.Platform; run the test project directly rather than
via `dotnet test`.)

## Native AOT

The worker publishes as a self-contained Native AOT binary
(`PublishAot=true`). All JSON goes through a source-generated serializer context;
there is no reflection-based serialization.
