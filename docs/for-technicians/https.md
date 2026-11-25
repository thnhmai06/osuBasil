# HTTPS and Certificates

This page is the authoritative reference for TLS on Basil. If this document conflicts with another document, **this
document takes precedence**.

## Why and how

Basil holds no certificate and speaks no TLS: it serves plain HTTP and plain IRC inside the Compose network, and the
`nginx` service in front of it ends TLS for every HTTP subdomain (port `443`) and for IRC (port `6697`). The
certificate, the HSTS header and the redirect from port `80` are nginx's; its configuration is in
[`docker/nginx/`](../../docker/nginx/).

osu! stable requires HTTPS on port `443`. The certificate must cover the Basil domain and every sub-domain the server
serves:

```text
<domain>
c.<domain>
ce.<domain>
c4.<domain>
c5.<domain>
c6.<domain>
osu.<domain>
b.<domain>
a.<domain>
api.<domain>
assets.<domain>
irc.<domain>
```

The names must be Subject Alternative Names. A certificate for only `localhost` or only the main domain is **not
sufficient**.

A self-signed certificate must also carry `basicConstraints` with `CA:TRUE`. A Linux client's trust store (p11-kit, the
basis of `update-ca-trust` and the browsers) only places certificate authorities into its trust bundles, so a
self-signed certificate without it cannot become trusted there.

### Public deployment

Use a certificate from a trusted certificate authority. A wildcard certificate works when it covers both `<domain>` and
`*.<domain>`, and clients must trust it without installing a custom CA.

### LAN-only deployment

A self-signed certificate is fine on a private network, but it must still carry every SAN above, and every client must
trust the certificate or its issuing CA. See
[`getting-started.md`](../for-client/osu-stable/getting-started.md).

---

## IRC

IRC is TLS only, on port `6697`, ended by nginx's `stream` block, which forwards plain TCP to Basil's internal IRC port
(`Basil:Server:Irc:Port`, default `6667`, never published) behind a PROXY protocol header that carries the real client
address. Basil closes every IRC connection that does not start with that header from a trusted proxy, so there is no
plaintext path from outside. The same certificate serves `irc.<domain>`; a client must connect with TLS to port `6697`,
which tools that only speak plaintext IRC cannot do. A certificate from Cloudflare's Origin CA does not serve IRC, since
IRC clients reach nginx directly; keep the `irc.` DNS record DNS-only. See
[`../for-developers/irc.md`](../for-developers/irc.md).

---

## Default certificate

nginx ships with a self-signed certificate for local development and private LANs:

```text
docker/nginx/certs/basil.local.crt
docker/nginx/certs/basil.local.key
```

It covers `basil.local`, `*.basil.local` and `irc.basil.local`, and declares `basicConstraints` `CA:TRUE`, so Linux
clients can trust it through their system certificate store. If you change `Basil:Server:Domain`, you need a certificate
for the new domain.

> [!IMPORTANT]
> **Do not use the default certificate for production or a publicly exposed server.**
>
> Its private key is in the repository and is therefore public. Anyone holding it can impersonate a server to clients
> that trust it.

---

## Use your own certificate

Mount your certificate and key over the two files nginx reads, in `docker-compose.yml` (a certificate chain goes in
the `.crt` file; it must cover every name above):

```yaml
  nginx:
    volumes:
      - /path/to/fullchain.pem:/etc/nginx/certs/basil.local.crt:ro
      - /path/to/privkey.pem:/etc/nginx/certs/basil.local.key:ro
```

then run `docker compose restart nginx`. Basil needs no change and no restart. A wildcard certificate for `<domain>` and
`*.<domain>` from a public authority works, and Basil has no ACME client: obtain and renew the certificate yourself.

### Behind Cloudflare

Cloudflare may proxy the HTTP records. Mount `docker/nginx/cloudflare.conf` over `/etc/nginx/real-ip.conf` in the
`nginx` service: nginx then takes the client address from `CF-Connecting-IP` for requests that come from Cloudflare's
ranges and passes the country from `CF-IPCountry` on to Basil. Without it, nginx ignores those headers and Basil never
sees them. Keep `irc.<domain>` DNS-only (grey cloud) because Cloudflare does not carry IRC.

---

## Generate a certificate for a private LAN

Replace `basil.local` with your domain. The certificate must include every name listed under
[Why and how](#why-and-how).

### Windows

Generate the pair with OpenSSL (for example in WSL or Git Bash) as shown below, or export a PowerShell certificate to
PEM, then follow [Use your own certificate](#use-your-own-certificate). Trust it on every client:

```powershell
Import-Certificate -FilePath "basil-cert.pem" -CertStoreLocation "cert:\LocalMachine\Root"
```

### macOS / Linux

```bash
domain="basil.local"

openssl req -new -x509 -days 365 -nodes \
  -out basil-cert.pem \
  -keyout basil-key.pem \
  -subj "/CN=$domain" \
  -addext "basicConstraints=critical,CA:TRUE" \
  -addext "subjectAltName=DNS:$domain,DNS:c.$domain,DNS:ce.$domain,DNS:c4.$domain,DNS:c5.$domain,DNS:c6.$domain,DNS:osu.$domain,DNS:b.$domain,DNS:a.$domain,DNS:api.$domain,DNS:assets.$domain,DNS:irc.$domain"

```

Trust it on the client:

```bash
# macOS
security add-trusted-cert -d -r trustRoot -k ~/Library/Keychains/login.keychain basil-cert.pem

# Linux (Debian/Ubuntu)
sudo cp basil-cert.pem /usr/local/share/ca-certificates/basil.crt
sudo update-ca-certificates

# Linux (Arch)
sudo cp basil-cert.pem /etc/ca-certificates/trust-source/anchors/basil.crt
sudo update-ca-trust extract
```

Then mount `basil-cert.pem` and `basil-key.pem` as described in [Use your own certificate](#use-your-own-certificate).

---

## Trusting the proxy

Basil believes the forwarded client address only from the addresses in `Basil:Server:TrustedProxies` (the Compose file
sets it to the compose network). nginx sends the real client in `X-Real-IP` and the scheme in `X-Forwarded-Proto`; Basil
removes `X-Real-IP`, `X-Forwarded-*`, `CF-*` and `X-Country-Code` from every request that does not come from a trusted
proxy. See [`configuration.md`](configuration.md#available-settings).

---

## Troubleshooting

### nginx does not start after changing the certificate

1. The two mounted files exist and nginx can read them.
2. The key matches the certificate and is not password protected.
3. `docker compose logs nginx` names the error.

### The client reports a certificate error

1. The certificate contains the exact hostname the client uses.
2. The hostname resolves to this server.
3. The client trusts the certificate.
4. It has not expired.
5. The client connects over HTTPS to port `443` (IRC over TLS to `6697`).

For LAN deployments the usual cause is a certificate that is not trusted on the client.

---

## See also

* [`configuration.md`](configuration.md) — deployment settings and data directories
* [`deployment.md`](deployment.md) — complete deployment procedure
* [`docker.md`](docker.md) — Docker deployment and certificate mounts
* [`../for-client/osu-stable/getting-started.md`](../for-client/osu-stable/getting-started.md) — client setup and trusting a LAN certificate
* [`troubleshooting.md`](troubleshooting.md) — general deployment troubleshooting
