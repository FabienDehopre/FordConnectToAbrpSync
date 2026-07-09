# Hand-written slim HTTP clients instead of Kiota generation

Both API clients are hand-written typed `HttpClient`s with slim DTOs covering
only the mapped fields, serialized via `System.Text.Json` source-generation.
Kiota was considered (Ford ships an OpenAPI spec) but rejected: the project
targets Native AOT (`PublishAot=true`) and Kiota's runtime is not AOT-verified;
ABRP has no OpenAPI spec at all; and a Ford→ABRP mapping layer is needed
regardless, so Kiota would only generate the cheap input DTO while emitting a
large model and reflection-based serialization that endanger the AOT build.
Cost accepted: manual DTO upkeep if we later map additional Ford metrics.
