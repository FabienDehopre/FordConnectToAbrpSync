# Ford access via authorization-code flow with a persisted refresh token

Although Ford's OpenAPI advertises client-credentials, real access to
`/v1/telemetry` requires a user to authorize the app against a vehicle in the
FordPass portal — an interactive authorization-code flow with a `localhost`
redirect. We therefore split auth into a one-time **Login** command (spins up an
`HttpListener` on a configurable localhost port — `Ford:LoopbackPort`, default
19579, which must match the app registration's redirect URI — exchanges the
code for a refresh token) and a headless **Run** that mints access tokens from
that refresh token.
Ford rotates the refresh token on some refreshes, so it is persisted in an
encrypted **Token Store** and rewritten on every rotation. Chosen over pasting a
static token (which would expire and strand the worker) and over embedding an
interactive browser in the container (impractical headless).
