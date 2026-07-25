# Docker healthcheck via self-invoked subcommand and heartbeat file

The Run mode has no HTTP server (it's a headless `BackgroundService`, not
ASP.NET Core), and the runtime image is `mcr.microsoft.com/dotnet/runtime-deps`
with no `curl`/`wget`, so the usual `HEALTHCHECK CMD curl localhost/health`
pattern doesn't apply and adding an HTTP listener or shell tooling just for
this would cost more than it's worth. Instead, each Sync Cycle writes a
liveness deadline (`now + 3 × Interval`) to `/app/heartbeat`
(`Health/HeartbeatWriter.cs`), and `HEALTHCHECK` invokes the same published
binary with a `healthcheck` argument (`Health/HeartbeatCheck.cs`), which reads
that file and exits 0/1 — no new dependencies, no open port. The deadline is
3 intervals out deliberately: the heartbeat means "the loop is still running,"
not "the last Sync Cycle succeeded," so a transient Ford/ABRP API outage
doesn't flip the container unhealthy and trigger a restart loop. The
`healthcheck` argument is handled before `Host.CreateApplicationBuilder` runs,
so the check process never touches Serilog's file sink or the Data Protection
key ring that the live worker process holds.
