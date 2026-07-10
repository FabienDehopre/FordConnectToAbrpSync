# Serilog configured in code, not appsettings, for Native AOT

Logging moved from `Microsoft.Extensions.Logging` to Serilog (console + rolling
file sinks). The logger is wired entirely with Serilog's fluent C# API in
`Program.cs` rather than the idiomatic `ReadFrom.Configuration(appsettings.json)`,
because this project targets Native AOT (`PublishAot=true`) and the settings
reader (`Serilog.Settings.Configuration`, and `LoggerConfiguration.KeyValuePairs`)
is annotated `RequiresDynamicCode`/`RequiresUnreferencedCode` — it scans
assemblies by name at runtime to resolve sinks, which trimming removes and AOT
forbids. A future reader will expect the JSON-driven setup and wonder why it's
absent; the reason is the AOT build stays warning-free only when every sink type
is statically referenced. The one runtime knob kept in configuration is the
overall minimum level (`Serilog:MinimumLevel:Default`), read as a plain scalar
(AOT-safe); namespace overrides are constants in code. Cost accepted: adding or
retuning a sink is a code change plus rebuild, not a config edit.
