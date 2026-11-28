# W1: Basil behind nginx (edge network)

Source of truth: `plans/review/release-1.0.md` decisions D23, D29, D30, D34, D36, D37, D38 and findings F-087, F-098,
F-102, F-116, F-117, F-069, F-070, plan P2 steps 1, 2, 4, 5. Read those rows before starting. Read `AGENTS.md` fully.

## Goal

Basil runs only as a container behind nginx. nginx terminates TLS (HTTP subdomains and IRC on 6697), holds the
per-address limits and serves stored files. Basil speaks plain HTTP and plain IRC on internal ports, trusts only the
nginx peer, and keeps no secret and no certificate.

## Out of scope (later waves, do not touch)

- The `Data/` layout, `Configurations/` merge, `Templates/` → `Localizations/`, covers to `Cache/` (W3).
- Uploads and `IUploadSpool` (W4). The rulesets stack (W5). Gateway rework, `AdmissionSubject` by IP (W10).
- The console dashboard beyond removing `restart` (W11). Tests (W12). Any wording outside the strings listed here.

## Steps

### 1. Remove what Docker-only makes dead (D29, D37)

- Velopack: package (`Directory.Packages.props`, `Basil/Basil.csproj`), `VelopackApp.Build().Run()` in `Program.cs`, the
  Velopack path of `Basil/Configuration/UpdateChecker.cs`. The app update check now asks the GitHub releases API
  (`Basil:Updates:Source` repository, latest release tag) through `IHttpClientFactory` with a timeout, compares with
  `AppVersion`, and only reports in `IUpdateStatus`. Any failure is logged once and the check turns itself off; it never
  throws out of the hosted service (AGENTS.md "Optional features never stop the server"). Drop the `Channel` option.
- Restart: `Basil/Lifetime/Restarter.cs`, its use in `Program.cs`, `ShutdownController.RestartRequested` and exit code
  75, the `restart` console command (`Basil.Host.Cli/Commands/MetaCommands.cs` and its catalog/help entries), the
  restarting branch of `GracefulShutdown`, and `IrcReplies.Restarting*` if nothing else uses them. Keep
  `RestartAnnouncer` in Host.Stable if shutdown still tells osu! clients to reconnect. Keep `ConfigurationWatcher`'s
  "needs a restart" notion (it now means restart the container).
- mDNS: `Basil/Configuration/LanAdvertiser.cs`, its registration in `HostApplication.cs`, `ServerOptions.Multicast`,
  the package, the schema and settings entries.
- HTTPS in Kestrel: `KestrelSetup.UsesHttps`, `UsesPublishedCertificate`, `PublishedCertificatePassword`, `Bindings`
  (if only `Restarter` used it), `UseHsts`, the certificate warning in `HostApplication.cs`, the `Kestrel:Certificates`
  block. Kestrel listens on `http://0.0.0.0:8080` (non-root can bind it). F-102 is closed by this: HSTS is nginx's job.
- The certificate: convert `Basil/Configurations/basil.local.pfx` (password `basil`) or generate a new self-signed one
  (10 years) to PEM for nginx with SANs `basil.local`, `*.basil.local` and `irc.basil.local`, place it under
  `docker/nginx/certs/` (`basil.local.crt`, `basil.local.key`), then delete the pfx and its csproj copy entry.

### 2. Secrets out of `appsettings.json` (D36, F-098)

- Remove `ConnectionStrings` from `Basil/Configurations/appsettings.json` and the schema's defaults. Keep binding from
  configuration (environment variables `ConnectionStrings__Read`, `ConnectionStrings__Write` work already through the
  standard providers; check `ConfigurationSetup.Add`).
- Missing connection string at start: fail with one plain sentence naming the variable (not a stack trace).
- Compose takes them from `.env`: commit `.env.example` (`POSTGRES_PASSWORD=change-me`), add `.env` to `.gitignore`.
  Remove the `command:` connection strings.
- Grep for any other secret (passwords, keys, tokens) in `appsettings.json`, compose and the Dockerfile; move each to env.

### 3. One client-address resolver, trust only nginx (F-087, D30)

- Move `Basil.Host.Stable/Support/ClientAddress.cs` into `Basil.Host.Shared` (a folder by concept, e.g. `Http/`), make
  `Caller.Ip()` in `Basil.Host.Rest/Http/Caller.cs` and every Stable caller use it. Delete the duplicate.
- The resolver reads only `X-Real-IP` (nginx sets it to the real client after its own `real_ip` processing, also behind
  Cloudflare) and falls back to the peer address. No `X-Forwarded-For` parsing, no `CF-Connecting-IP`.
- `TrustedProxiesMiddleware` keeps stripping forwarding headers (`X-Real-IP`, `X-Forwarded-*`, `CF-*`, `X-Country-Code`)
  from untrusted peers. Country stays read from `CF-IPCountry`/`X-Country-Code` only when the peer is trusted (nginx
  forwards it only in the Cloudflare variant and clears it otherwise).
- `X-Forwarded-Proto` from a trusted peer decides the scheme of every absolute URL Basil builds (find where URLs are
  composed from `Domain`; use the request's scheme after `UseForwardedHeaders`-style handling, or the header directly).
- Default `TrustedProxies` in `appsettings.json` stays loopback; compose sets `Basil__Server__TrustedProxies__0` to the
  compose network's subnet (declare a fixed subnet for the network).

### 4. Per-address limits leave Basil (D30)

Remove the IP-keyed rate rules nginx now holds: `rest-anonymous:{ip}` in `Basil.Host.Rest/Http/RestPipeline.cs` and its
`PerIp` settings, the per-address cap in `Basil.Host.Rest/Sse/SseHub.cs`, `login-fail:{ip}` in
`Basil.Host.Stable/Web/WebAuthentication.cs` (keep any per-account rule), `rateLimits.registration.perIp.*` settings and
labels. Keep `AdmissionRefusals.SubjectOf(ip)` subjects as they are (W10 reworks them).

### 5. IRC behind nginx: PROXY protocol (D30, D34)

- A PROXY protocol v1 header parser (text form `PROXY TCP4|TCP6 <src> <dst> <sport> <dport>\r\n`, max 107 bytes) in
  `Basil.Protocol.Irc` (pure, no I/O: bytes in, parsed source address or failure out).
- `Basil.Host.Irc/Transport/IrcListener.cs`: every connection must start with a valid header from a trusted proxy peer,
  read with a short deadline (5 s); otherwise close without a reply. The parsed source address is the session's
  address. Internal port stays `Basil:Server:Irc:Port` = 6667; the public port 6697 is nginx's.
- No plaintext public path: compose publishes only nginx.

### 6. Stored files through `X-Accel-Redirect` (D30)

- Find every route that sends a stored file (images, avatars, covers, replays, `.osz`, screenshots; `Results.File`,
  `PhysicalFile`, `SendFileAsync`, the image middleware).
- New setting `Basil:Server:Files:AccelPrefix` (string, null by default, set by compose to `/_files/`): when set, the
  route answers with an empty body and `X-Accel-Redirect: <prefix><path relative to the data root>` plus the same
  `Content-Type`, `Content-Disposition` and cache headers; when null it serves the file as today (`dotnet run` without
  nginx). One helper in `Basil.Host.Shared` used by every such route.
- SSE responses add `X-Accel-Buffering: no` (find the one SSE writer in `Basil.Host.Rest/Sse`).

### 7. nginx, compose, Dockerfile (P2 steps 1, 4, 5; F-069, F-070, F-116, F-117)

- `docker/nginx/nginx.conf` (or `conf.d/basil.conf` + `stream` include):
  - `http`: `server_name basil.local *.basil.local` on 443 with the default certificate (operator replaces by mount),
    port 80 redirects to 443 except what the osu! client needs over HTTP (check `docs/for-technicians/https.md` for the
    osu! client's rules and keep them); `proxy_pass http://basil:8080` with `Host`, `X-Real-IP $remote_addr`,
    `X-Forwarded-Proto $scheme`, cleared `CF-*` headers; upstream `keepalive 32` and `keepalive_timeout 120s` (below
    Kestrel's 130 s); `proxy_http_version 1.1`, `Connection ""`; `proxy_buffering off` and long `proxy_read_timeout` on SSE
    routes; `client_max_body_size` per upload route (osz 100m, screenshots 10m, avatars 5m, default 1m);
    `client_body_timeout`/`client_header_timeout`; `limit_req_zone`/`limit_conn_zone` by `$binary_remote_addr` with a
    stricter zone for login and registration routes; `location /_files/ { internal; alias /data/; }` over the read-only
    `Data/` mount.
  - `stream`: `listen 6697 ssl` with the same certificate, `proxy_pass basil:6667`, `proxy_protocol on`, `limit_conn`.
  - `docker/nginx/cloudflare.conf`: variant include with `set_real_ip_from` for Cloudflare ranges, `real_ip_header
    CF-Connecting-IP`, and `CF-IPCountry` forwarded. The IRC record stays DNS-only (document it).
- `docker-compose.yml`: services `nginx` (official `nginx:stable-alpine`, mounts config, certs, `Data/` read-only;
  publishes 80, 443, 6697), `basil` (no published ports, `tty: true`, `stdin_open: true`, env from `.env`, the
  `TrustedProxies` subnet, `Files__AccelPrefix`, healthcheck), `postgres` (password from `.env`). `restart: on-failure`
  everywhere (F-069). Keep `stop_grace_period: 90s`. Keep the existing volume paths for `Data`/`Logs`/`Templates`
  (W3 changes them).
- `Dockerfile`: `EXPOSE 8080 6667`; `HEALTHCHECK` with `curl -sf -H "Host: api.<domain>" http://127.0.0.1:8080/health`;
  run as the `app` user of the .NET image (`USER app`, files group-readable so nginx's read-only mount can serve them;
  set `umask`/permissions on `Data/` accordingly); drop `.pdb`, `.xml`, `createdump` from the publish output
  (`DebugType=none`, `GenerateDocumentationFile` off for publish, remove `createdump`); ffmpeg installed in the runtime
  stage only (no curl if the healthcheck can avoid it; otherwise keep curl).

### 8. Docs that change with this work

Update only the parts this wave makes false: `docs/for-technicians/docker.md`, `https.md` (nginx holds TLS; default
certificate; how to mount your own; Cloudflare variant), `configuration.md` (removed keys, new env variables,
`Files:AccelPrefix`), `AGENTS.md` (Hosts section: IRC is plain TCP behind nginx with a required PROXY header; the
composition root no longer chooses TLS or restarts). Prose, Why → What → How; no new competing pages.

## Rules for you

- Tools: Rider MCP for navigation and refactoring (`search_symbol`, `find_usages`, `rename_refactoring`,
  `safe_delete`, `get_file_problems`, `build_solution_start`/`build_solution_state`). OpenCode Go server at
  `http://localhost:4096` with cheap flash models only, for mechanical edits you can describe exactly (deleting a
  reference set, moving a file and fixing usings). You review what it returns.
- Build only: `dotnet build Basil.slnx -c Release --nologo -v q` must pass with no new warnings. Do not run tests.
- Do not commit, do not `git add`. Leave the working tree for review.
- AGENTS.md rules apply: no `.ConfigureAwait(false)`, keep final newlines and formatting, no reflowed lines, no renamed
  parameters outside the task, no `TODO`, no member left unused by your change, user-visible strings in their reply
  class.
- When a step needs a decision not written here or in the review, stop that step, finish the rest, and report it.

## Report

Return: what changed per step (files), what you could not do and why, every decision you took that the brief did not
state, and the build result.
