# FordConnectToAbrpSync

A .NET 10 worker that polls a Ford vehicle's telemetry and relays the
route-relevant parts to [A Better Route Planner (ABRP)](https://abetterrouteplanner.com)
for live journey tracking. It only sends to ABRP when the data meaningfully
changes, and both HTTP clients are resilient to rate limiting and transient
errors.

## What you need

- A Ford vehicle with connectivity (FordPass Connect) added to your FordPass
  account.
- A **Ford app registration** (client id + secret) — see
  [Ford app registration](#ford-app-registration) below.
- An **ABRP user token** for your vehicle: in the ABRP app or website, open
  your car's settings and use the *live data* / *generic* link option to
  generate a token.
- An **ABRP partner API key**, issued by [Iternio](https://www.iternio.com)
  (the makers of ABRP) on request.
- The [.NET 10 SDK](https://dotnet.microsoft.com/download) to build and run
  the one-time login (Docker alone is not enough — the login must run on your
  machine). The exact SDK version is pinned in [`global.json`](./global.json).
- Optionally, Docker + Docker Compose to run the sync as a container.

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

Create a Ford app registration whose redirect URI is
`http://localhost:19579/`. The port comes from `Ford:LoopbackPort` in
[`appsettings.json`](./FordConnectToAbrpSync/appsettings.json) (default
`19579`); if you override it, the registered redirect URI must match.
Authorize the registration against your vehicle in the FordPass portal. The
requested scope must include `offline_access` so Ford issues a refresh token.

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
ring in `./data/keys`). You only need to repeat it if the stored refresh token
stops working.

### 2. Run the sync

```bash
dotnet run --project FordConnectToAbrpSync
```

The worker logs to the console and to daily rolling files under `./logs`
(31 days retained).

### Docker

```bash
cp .env.example .env      # fill in the four secrets
# Log in once on the host to populate ./data (same volume the container mounts):
dotnet run --project FordConnectToAbrpSync -- login
docker compose up -d --build
```

The `.env` names (`FORD_CLIENT_ID`, `ABRP_TOKEN`, …) are mapped onto the
`Ford__*` / `Abrp__*` environment variables in [`compose.yaml`](./compose.yaml),
which also shows the optional overrides.

The container mounts `./data` for the token + key ring and `./logs` for the log
files, and reads secrets from the environment. Because the `login` flow needs
an interactive browser and a loopback redirect, run it on the host as shown;
the container only ever runs the headless sync.

## Troubleshooting

- **Check what Ford is actually reporting**: `dotnet run --project
  FordConnectToAbrpSync -- test` fetches one telemetry snapshot and dumps the
  raw JSON to stdout (logs go to stderr, so the JSON stays pipeable).
- **ABRP shows charging as driving (or vice versa)**: set
  `Sync:InvertPowerSign` to `true` — some vehicles report the power sign
  reversed.
- **Nothing reaches ABRP while parked**: expected — the worker skips relays
  when the watched values haven't changed and when Ford hasn't produced a
  newer snapshot. Check the logs in `./logs` (or `docker compose logs -f`)
  to see each cycle's decision.
- **Login/auth errors**: re-run the `login` command to store a fresh refresh
  token.

## For developers

See [`CONTEXT.md`](./CONTEXT.md) for the domain glossary and
[`docs/adr/`](./docs/adr) for the key design decisions.

### Tests

```bash
dotnet run --project FordConnectToAbrpSync.Tests
```

(TUnit uses Microsoft.Testing.Platform; you can run the test project directly (as above) or via `dotnet test`.)

### Native AOT

The worker publishes as a self-contained Native AOT binary
(`PublishAot=true`). All JSON goes through a source-generated serializer context;
there is no reflection-based serialization.