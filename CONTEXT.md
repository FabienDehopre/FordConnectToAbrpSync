# Ford → ABRP Sync

A background worker that reads a Ford vehicle's telemetry and relays the
meaningful parts to A Better Route Planner (ABRP) for live journey tracking.
It exists to bridge two vehicle-data services that don't talk to each other.

## Language

**Telemetry Snapshot**:
One reading of the vehicle's state fetched from Ford in a single poll — a bag
of named metrics (state of charge, position, charging status, …), each stamped
with its own report time.
_Avoid_: status, payload, data

**Metric**:
A single named measurement inside a Telemetry Snapshot, carrying a value and the
time the vehicle last reported it.
_Avoid_: field, property, signal

**Sync Cycle**:
One iteration of the worker: acquire access, fetch a Telemetry Snapshot, decide
whether it is worth relaying, and relay it if so.
_Avoid_: tick, loop, run, iteration

**Watched Subset**:
The small set of route-relevant values (state of charge, power, speed,
latitude, longitude, charging, DC fast charging, parked) whose change is what
makes a Snapshot worth relaying. Changes outside this set never trigger a relay.
_Avoid_: tracked fields, diff set

**Relay**:
Sending a mapped Snapshot onward to ABRP. Only happens when the Watched Subset
has meaningfully changed since the last successful Relay.
_Avoid_: push, publish, upload, send

**Baseline**:
The Watched Subset of the last successfully Relayed Snapshot, held in memory and
compared against each new Snapshot to detect meaningful change.
_Avoid_: previous state, last value, cache

**Stale Snapshot**:
A Snapshot whose report time has not advanced since the previous fetch — the
vehicle has not reported anything new. Discarded before any comparison.
_Avoid_: unchanged, old, duplicate

**Meaningful Change**:
A difference in the Watched Subset that exceeds its noise tolerance (e.g. GPS
jitter and sub-half-percent charge drift do not count). Only a Meaningful Change
justifies a Relay.
_Avoid_: any change, delta

**Token Store**:
The durable, encrypted home of the Ford refresh credential, surviving restarts
and rewritten whenever Ford rotates it.
_Avoid_: token file, cache, secrets

**Login**:
The one-time interactive act of authorizing the worker against Ford, producing
the first refresh credential that later Sync Cycles run on. Distinct from the
headless Run.
_Avoid_: auth, sign-in, connect

**Run**:
The headless, ongoing mode of the worker that performs Sync Cycles. Never
prompts interactively; refuses to start if no Login has happened.
_Avoid_: start, serve

**Idle**:
The vehicle condition — ignition off and no charging in progress — in which
Snapshots stop changing and the worker polls at the slower Idle Interval.
Any other ignition reading (including unknown) or an active charge counts as
not Idle.
_Avoid_: parked, asleep, inactive

**Idle Interval**:
The stretched poll cadence used while the vehicle is Idle. The normal poll
interval resumes as soon as the vehicle is no longer Idle or a Wake Signal
arrives.
_Avoid_: slow interval, sleep interval

**Wake Signal**:
An external nudge from the driver's phone announcing the vehicle is about to
be used. Triggers an immediate Sync Cycle and opens a Boost Window.
_Avoid_: ping, trigger, webhook

**Sleep Signal**:
An external nudge from the driver's phone suggesting the vehicle is done being
used. Closes any open Boost Window and triggers an immediate Sync Cycle for
re-evaluation; never forces the Idle Interval by itself.
_Avoid_: stop, pause

**Boost Window**:
The bounded period after a Wake Signal during which the worker keeps the
normal poll interval even while the vehicle still reports Idle — cover for the
lag between sitting down in the car and Ford's cloud noticing.
_Avoid_: grace period, override
