# 7. Idle pacing with external Wake/Sleep Signals over a slim web host

Date: 2026-07-27

## Status

Accepted

## Context

The worker polled Ford at a fixed interval (60 s) around the clock. A parked,
non-charging vehicle produces near-frozen telemetry, so the vast majority of
those polls fetched nothing worth relaying while still spending Ford API quota
— roughly 1,400 wasted calls on a day the car isn't driven.

Simply stretching the interval when the ignition is off creates the opposite
problem: the first drive after a long stop goes untracked for up to a full
slow interval. The driver's phone, however, knows the drive is starting the
moment it connects to the car (CarPlay/Bluetooth), and iOS Shortcuts
automations can fire an HTTP request at exactly that moment, hands-free. That
requires the worker to expose an HTTP endpoint — but the app was a plain
console host (`Host.CreateApplicationBuilder`, `Microsoft.NET.Sdk.Worker`)
with no HTTP stack, built with Native AOT, which rules out anything
reflection-heavy.

Ford's telemetry provides `ignitionStatus` (OFF/ON/ACCESSORY/UNKNOWN/
UNRECOGNIZED) and a charge status; the owner's vehicle is confirmed to report
OFF/ON reliably. Charging with the ignition off must keep the normal pace —
ABRP wants the live SoC curve during a session.

## Decision

Two cooperating mechanisms:

**Idle pacing.** A Snapshot showing ignition OFF and no charge in progress
marks the vehicle Idle; the loop then waits `Sync:IdleInterval` (default
30 min) instead of `Sync:Interval`. Every ambiguous reading (missing metric,
UNKNOWN, ACCESSORY) counts as active, so a misreport can only ever waste
polls, never starve tracking. A failed cycle keeps the last known state.

**External Signals.** The host became `WebApplication.CreateSlimBuilder`
(SDK `Microsoft.NET.Sdk.Web`, officially AOT-supported) serving two minimal
endpoints in Run mode only:

- `POST /wake` — immediate Sync Cycle plus a Boost Window
  (`Sync:BoostWindow`, default 10 min) during which the normal interval holds
  even while the vehicle still reports Idle. Covers the lag between the phone
  connecting and Ford's cloud noticing the ignition.
- `POST /sleep` — closes any Boost Window and triggers an immediate
  re-evaluation. Deliberately *not* a hard "go slow": a Bluetooth drop
  mid-drive fires the disconnect automation, and the follow-up cycle seeing
  ignition ON keeps the normal pace.

Both require a bearer secret (`Signal:Secret`) checked in-app with a
fixed-time comparison; unset means fail closed. Internet exposure is the
operator's Cloudflare tunnel; the in-app check keeps the endpoints closed
even if the tunnel config drifts. The heartbeat deadline scales with the
upcoming wait so the Docker healthcheck tolerates Idle pacing.

## Consequences

- Ford API calls drop ~96% while the vehicle sits Idle; a missed Wake Signal
  (passenger drive, dead phone, tunnel outage) is caught within one Idle
  Interval at worst.
- The binary carries ASP.NET Core now — larger artifact, and Kestrel listens
  on 8080 in Run mode (login/test/healthcheck never start the server).
- The phone-side automation is unowned by this repo: an iOS Shortcuts
  automation on CarPlay/Bluetooth connect/disconnect calling /wake and /sleep.
  If it silently breaks, everything still works, just at Idle-Interval
  latency.
- Signal endpoints are single-purpose nudges; they carry no vehicle data and
  return no body, so a leaked secret's blast radius is forced fast polling.
