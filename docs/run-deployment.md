# Run & Deployment

Basil is one ASP.NET Core process. No Docker, no separate DB/cache service — SQLite is a single
file, and everything else (replays, avatars, beatmaps, seasonal backgrounds, FAQ text) is a plain
folder next to the executable, auto-created on first startup. This doc has three parts: **Deployment**
(a real LAN tournament server other machines connect to), **Development** (working on Basil itself),
and **Client** (pointing an actual osu! stable install at either of the above).

## Configuration surface

Two files, both plain text, both live next to the executable (or in `src/Basil.Web/` in a dev
checkout) — no rebuild needed after editing either, just restart the process:

| File                | Owns                                                                                                      |
| ------------------- | ----------------------------------------------------------------------------------------------------------- |
| `appsettings.json`  | Framework config only — `Logging`, `AllowedHosts`. Standard ASP.NET Core convention, untouched.              |
| `Settings.toml`     | Everything Basil itself reads — `Server`, `Mirror`, `Bot`, `Irc`, `Database`. Same file for development and deployment; edit it and restart. |

There is **no environment-variable override layer** — `Settings.toml` is the single source of
truth for every setting. Edit the file directly and restart the process.

`Settings.toml` sections, as shipped:

| Section          | Key(s)                              | Meaning                                                                                                             |
| ----------------- | ------------------------------------ | --------------------------------------------------------------------------------------------------------------------- |
| `[Server]`        | `Domain`                             | Public hostname clients connect to — every subdomain (`c./ce./c4./c5./c6./osu./a./b./api.`) hangs off this.        |
|                    | `Port`                               | Kestrel HTTPS listen port (default 443). Disables automatic port selection — server binds exclusively here.       |
|                    | `CertPath` / `CertPassword`          | Path to HTTPS cert (PFX) and its password. Leave both unset to use the ASP.NET Core dev cert or OS-level reverse proxy TLS. |
|                    | `MenuIconPath` / `MenuOnclickUrl`    | osu! client's in-game menu icon (top-left). `MenuIconPath` is a **local file path**, not a URL — served back over HTTP at `GET /web/menuicon` on the `osu.` host; the client is pointed at that URL, not the path. `MenuOnclickUrl` is the click-through URL opened when clicked. Cosmetic only. |
| `[Bot]`            | `Name`                               | BasilBot's display name. Changing this after first boot renames the seeded `id=0` user in-place.                   |
|                    | `CommandPrefix`                      | Prefix chat commands must start with (`!help`, `!roll`, `!mp ...`).                                                 |
|                    | `Country`                            | BasilBot's country code (default `"vn"`). Overrides seed migration value.                                          |
| `[Irc]`            | `Name`                               | IRC server name.                                                                                                   |
|                    | `Port`                               | TCP port for the embedded IRC gateway (default 6667).                                                              |
| `[Server]`         | `AdminKey`                           | Gates every `api.<domain>` management REST route (beatmap/user/replay/match/seasonal CRUD) via `X-Admin-Key` **and** acts as the secret for in-game registration (osu! client's Email field). Unset = management API locked down + registration disabled. **Configure this to allow account creation.** |
| `[Database]`       | `Path`                               | SQLite file path. Relative paths resolve next to the executable. Default `basil.db`, rarely needs changing.        |
| `[Mirror]`         | `DownloadEndpoint`                   | Optional external `.osz` mirror for `/d/{set_id}`. Unset by default — Basil runs fully offline, downloads report "unavailable" instead of reaching the internet. |

### Data (always fixed, never configurable)

Created automatically next to the executable on startup if missing — no manual setup:

| Path         | Contents                                                                 |
| ------------- | --------------------------------------------------------------------------- |
| `basil.db`    | SQLite database (+ `basil.db-wal`/`basil.db-shm` while running). Migrations run automatically on every startup. |
| `Replays/`    | Score replay files.                                                      |
| `Avatars/`    | User avatar files (`{userId}.{ext}`).                                    |
| `Mapsets/`    | Beatmap files (`.osu`/`.osz`) — drop files here, they're ingested automatically on the next startup, or push them live via `POST /beatmaps` on `api.` (admin-key). |
| `Seasonals/`  | Seasonal background images shown in the client's main menu.              |
| `Faqs/`       | `!faq <entry>` text files (`<entry>.txt`).                               |

To move a deployment to another machine: stop the server, copy the whole executable directory
(including `basil.db*` and the five folders above) to the target, start it there.

---

## 1. Deployment — running a real server for others to connect to

### Steps

1. **Publish** a framework-dependent build for the target OS. The target machine needs the
   [.NET 10 ASP.NET Core Runtime](https://dotnet.microsoft.com/) installed (not the SDK):

   ```bash
   dotnet publish src/Basil.Web -c Release -r win-x64 --self-contained false -o publish/win-x64
   # or:
   dotnet publish src/Basil.Web -c Release -r linux-x64 --self-contained false -o publish/linux-x64
   ```

   Copy the `publish/<rid>/` folder to the target machine (or publish directly on it).

2. **Edit `Settings.toml`** next to the published executable:
   - `Server.Domain` — the real domain (or LAN hostname) clients will connect to, e.g.
     `tourney.example` or a plain LAN name like `basil.lan`. This single value drives every
     subdomain (`c./ce./c4./c5./c6./osu./a./b./api.`) — see [`api-client.md`](api-client.md)
     for exactly how.
    - `Server.AdminKey` — set this to a real secret. Without it the management API (used to create
       user accounts, since in-game registration requires an AdminKey — see Client setup) stays 401-locked.
   - `Bot.Name` / `Bot.CommandPrefix`, `Server.MenuIconPath`/`MenuOnclickUrl`, `Irc.Name`/`Irc.Port` — cosmetic,
     optional.

3. **Get a TLS certificate covering the domain and all 9 subdomains.** osu! stable only connects
   over **HTTPS on port 443** — plain HTTP is silently rejected before it reaches Kestrel, and the
   client checks the exact subdomain it connects to, so a generic `CN=localhost`-style cert won't
   work. The subdomains a cert needs SAN entries for:

   `<domain>`, `c.<domain>`, `ce.<domain>`, `c4.<domain>`, `c5.<domain>`, `c6.<domain>`,
   `osu.<domain>`, `b.<domain>`, `a.<domain>`, `api.<domain>`

   > [!NOTE]
   > The IRC gateway (`irc.<domain>`) listens on a separate TCP port (6667 by default, configurable
   > via `Irc.Port`) and is **not** served through ASP.NET Core/Kestrel — it binds a raw
   > `TcpListener` from `TcpIrcListener` (`BackgroundService`). It does not need TLS; a real IRC
   > client connects over plain TCP. No cert SAN entry is required for `irc.<domain>`.

   For a real public domain, any standard ACME/wildcard cert covering `*.<domain>` and `<domain>`
   works. For a LAN-only deployment without public DNS, generate a self-signed cert with those SANs
   — see the Development section below for the exact `openssl`/`New-SelfSignedCertificate` commands
   (same process, just swap `basil.local` for your real domain); every client machine will then need
   that cert installed as trusted (see Client setup).

4. **Point Kestrel at the cert** by setting `CertPath` and `CertPassword` in `Settings.toml`
   `[Server]` section (see Configuration surface above). The server binds exclusively to the
   port specified in `Server.Port` (default 443) — no `--urls` needed.

5. **DNS or hosts entries**: every machine that connects (the server itself, and every client) needs
   all 9 subdomains resolving to the server's address — either real DNS records for a public domain,
   or a `hosts` file entry per machine for a LAN-only setup (see Client setup below for the exact
   entries).

6. **Run it**. The port is read from `Settings.toml` `Server.Port` (default 443); no `--urls`
   argument needed. Binding a port below 1024 needs elevated privileges (Administrator on Windows,
   root or `setcap` on Linux):

   ```bash
   ./Basil.Web
   ```

   Open port 443 in the firewall if one is active.

7. **Verify it's up**: `curl -k https://osu.<domain>/web/bancho_connect.php` should return `200`
   (the client's own connectivity check).

8. **Create the first account.** Two ways:

   **a) In-game registration** — launch the osu! client pointed at this server (see Client setup
   below). On the login screen, click "Register". In the **Email** field, enter the value of
   `Server:AdminKey` from `Settings.toml`. Choose a username and password. The client will create
   the account with default privileges (`Unrestricted | Verified | Supporter`).

   **b) Admin API** — use `curl` (or any HTTP client) against the `api.` host:

   ```bash
   curl -X POST https://api.<domain>/users \
     -H "X-Admin-Key: <your Server.AdminKey>" \
     -H "Content-Type: application/json" \
     -d '{"name":"Player1","password":"hunter2"}'
   ```

   Optional fields: `"country": "VN"`, `"priv": 19` (default — see [`privileges.md`](privileges.md)).

   Every account is auto-verified (`Verified` flag added) on its own first login. No special
   first-user staff grant exists — grant staff privileges via `PUT /users/{id}` on the `api.` host
   with `"priv": 28683` (unrestricted + verified + supporter + moderator). See [`privileges.md`](privileges.md)
   for the full flag reference.

---

## 2. Development — working on Basil itself

1. **Run it:**

   ```bash
   dotnet run --project src/Basil.Web
   ```

   No external services to start — `basil.db` and the five storage folders are created next to the
   build output (`src/Basil.Web/bin/Debug/net10.0/`) on first run, migrations run automatically.

2. **`Settings.toml` is the same file used in production** — there's no separate dev-only config
   file. For local testing against a real osu! client, set `Server.Domain = "basil.local"`
   (or whatever local domain you're using) directly in `src/Basil.Web/Settings.toml`.

   The IRC gateway listens on port 6667 by default (configurable via `[Irc]` section in Settings.toml).
   Connect with any IRC client: `/server basil.local 6667` and authenticate with your account password.

3. **To connect an actual osu! client to your dev server**, you need a trusted cert and hosts
   entries, same requirement as Deployment above (the client itself doesn't know or care whether
   it's talking to a dev or production build). Generate a self-signed cert covering all 9
   subdomains (note: the IRC gateway uses a separate TCP port, no TLS, no cert needed):

   **PowerShell (Windows):**

   ```powershell
   $dnsNames = @("basil.local", "c.basil.local", "ce.basil.local", "c4.basil.local", "c5.basil.local",
                 "c6.basil.local", "osu.basil.local", "b.basil.local", "a.basil.local", "api.basil.local")
   $cert = New-SelfSignedCertificate -DnsName $dnsNames -CertStoreLocation "cert:\LocalMachine\My" -KeyExportPolicy Exportable
   Export-PfxCertificate -Cert "cert:\LocalMachine\My\$($cert.Thumbprint)" -FilePath "basil-cert.pfx" -Password (ConvertTo-SecureString -String "your-password" -Force -AsPlainText)
   Import-PfxCertificate -FilePath "basil-cert.pfx" -CertStoreLocation "cert:\LocalMachine\Root" -Password (ConvertTo-SecureString -String "your-password" -Force -AsPlainText)
   ```

   **Bash (macOS/Linux):**

   ```bash
   openssl req -new -x509 -days 365 -nodes -out basil-cert.pem -keyout basil-key.pem -subj "/CN=basil.local" \
     -addext "subjectAltName=DNS:basil.local,DNS:c.basil.local,DNS:ce.basil.local,DNS:c4.basil.local,DNS:c5.basil.local,DNS:c6.basil.local,DNS:osu.basil.local,DNS:b.basil.local,DNS:a.basil.local,DNS:api.basil.local"
   openssl pkcs12 -export -out basil-cert.pfx -inkey basil-key.pem -in basil-cert.pem -password pass:your-password
   # macOS: security add-trusted-cert -d -r trustRoot -k ~/Library/Keychains/login.keychain basil-cert.pem
   # Linux: install into your distro's trust store
   ```

   Point Kestrel at it by setting `CertPath`/`CertPassword` in `src/Basil.Web/Settings.toml`
   under `[Server]`. The server binds exclusively to the port specified in `Server.Port` (default
   443 — elevated terminal needed). Run normally:

   ```bash
   dotnet run --project src/Basil.Web
   ```

   Add hosts entries and launch the client — see Client setup below (same steps whether you're
   pointed at a dev or deployed server).

   > [!IMPORTANT]
   > Without a reverse proxy in front locally, the server synthesizes `X-Forwarded-For`/`X-Real-IP`
   > from the raw connection's remote address when neither header is present — otherwise
   > `Basil.Domain.ClientIpResolver.Resolve` throws, since it assumes (like bancho.py) that a proxy
   > always sets these headers in production.

4. **Run tests:**

   ```bash
   dotnet test tests/Basil.Domain.Tests
   dotnet test tests/Basil.Protocol.Tests
   dotnet test tests/Basil.Application.Tests
   dotnet test tests/Basil.ArchitectureTests
   dotnet test tests/Basil.IntegrationTests
   dotnet test tests/Basil.Infrastructure.Tests
   ```

   `Basil.Infrastructure.Tests` spins up a temp SQLite file per test class and deletes it
   afterward — no Docker daemon or external service needed. See [`CLAUDE.md`](../CLAUDE.md) for the
   recommendation to run this project in the foreground rather than backgrounded.

---

## 3. Client — connecting an actual osu! install

Applies identically whether the server is a Development or Deployment instance — the client only
cares about the domain, the cert, and the account.

1. **Use osu! stable** (the classic/legacy client, not lazer) — it's the one that supports the
   `-devserver` launch flag needed to point at a non-official server. Install it normally from the
   official osu! site.

2. **Trust the server's cert** on the client machine. For a self-signed dev/LAN cert, import the
   `.pfx`/`.pem` into the OS's trusted root store (Windows: `certmgr.msc` → Trusted Root
   Certification Authorities → Import; macOS: Keychain Access → System → drag in the cert, then
   mark "Always Trust"; Linux: your distro's CA trust update tool). For a real ACME-issued cert on a
   public domain, this step is unnecessary — it's already trusted.

3. **Resolve all 9 subdomains** to the server's address. For a LAN/self-signed setup, add hosts
   entries on the client machine (`C:\Windows\System32\drivers\etc\hosts` on Windows,
   `/etc/hosts` on macOS/Linux) pointing every subdomain at the server's IP:

   ```
   <server-ip> basil.local
   <server-ip> c.basil.local
   <server-ip> ce.basil.local
   <server-ip> c4.basil.local
   <server-ip> c5.basil.local
   <server-ip> c6.basil.local
   <server-ip> osu.basil.local
   <server-ip> b.basil.local
   <server-ip> a.basil.local
   <server-ip> api.basil.local
   ```

   (Substitute the real domain and swap `<server-ip>` for `127.0.0.1` if the client and server are
   the same machine.) For a real public domain, this step is unnecessary — normal DNS resolves it.

4. **Get an account.** In-game registration is available — launch osu! with `-devserver`, click
   "Register", and enter the server's `Server:AdminKey` in the **Email** field (see Deployment
   step 8 above). Alternatively, someone with the server's `AdminKey` can create the account via
   the admin API. Every account is auto-verified on its own first successful login — no extra step
   after that.

5. **Launch the client pointed at the server:**

   ```
   osu!.exe --debug -devserver <domain>
   ```

   (or the platform equivalent — `-devserver` is the flag that matters; it works identically on
   every OS the client ships for). Log in with the account from step 4.

Every client version is accepted — Basil doesn't check for a minimum/maximum client build (that
check proxied through osu!'s changelog API in bancho.py, which doesn't apply to a fully offline
server).
