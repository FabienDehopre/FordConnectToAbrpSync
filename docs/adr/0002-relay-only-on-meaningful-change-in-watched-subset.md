# Relay only on a Meaningful Change in the Watched Subset

Each Sync Cycle relays to ABRP only when a small route-relevant **Watched
Subset** (state of charge, power, speed, lat, lon, charging, DC fast charging,
parked) changes beyond noise tolerances, compared against an in-memory
**Baseline** of the last successful Relay. Two cheap guards precede the compare:
a **Stale Snapshot** short-circuit (Ford report time unchanged) and a
push-worthiness floor (skip when neither charge nor position is present).

Rationale: ABRP only needs updates when the driving/charging picture actually
moves; relaying every 60s poll would spam a rate-limited partner API and add
nothing. Excluded metrics (temps, voltage, current, odometer, range, heading)
still travel *in* the payload but never *trigger* a Relay. Baseline is in-memory
only — after a restart the first Cycle relays once, which is harmless, whereas a
persisted-but-stale Baseline after downtime would wrongly suppress a Relay.
