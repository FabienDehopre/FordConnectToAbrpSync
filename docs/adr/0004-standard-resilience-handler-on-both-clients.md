# Standard resilience handler, config-bound, on both HTTP clients

Both clients use `Microsoft.Extensions.Http.Resilience`'s
`AddStandardResilienceHandler`, bound to a config section (tunable and
hot-reloadable), rather than a hand-rolled Polly pipeline. It provides
rate-limiter → total-timeout → retry (exponential backoff + jitter, honoring
`Retry-After` on 429/503) → circuit-breaker → attempt-timeout, and already
treats 408/429/5xx + transient exceptions as retryable — covering the "resilient
to rate limiting and other errors" requirement with vetted defaults. The Ford
bearer-token refresh (401 → re-fetch → retry once) lives in a `DelegatingHandler`
placed *inside* the resilience handler, so a genuine 401 is not mistaken for a
transient fault and retried blindly.
