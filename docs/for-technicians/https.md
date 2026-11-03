# HTTPS and Certificates

This page is the authoritative reference for TLS on Basil. If this document conflicts with another document, **this
document takes precedence**.

## Requirements

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

IRC is a separate listener and **does not use TLS**: plain TCP on `Basil:Server:Irc:Port` (default `6667`). It needs no
`irc.<domain>` record, no certificate and no HTTPS configuration; clients connect to the server's address on that port.
The plain port is deliberate, and it means the IRC key crosses the network readable. See
[`../for-developers/irc.md`](../for-developers/irc.md).

---

## Bundled development certificate

Basil ships a self-signed certificate for local development and private LAN testing:

```text
Configurations/basil.local.pfx
```

It covers the required `basil.local` subdomains and declares `basicConstraints` `CA:TRUE`, so Linux clients can trust it
through their system certificate store. The bundled `Configurations/appsettings.json` already points at it with the
password `basil`, so a fresh local installation starts with HTTPS without generating anything.

The key and the password are public, so this certificate is only for local use. The server logs a warning at start while it serves HTTPS with it; replace it with your own certificate (see below) before exposing the server.

> [!IMPORTANT]
> **Do not use the bundled certificate for production or a publicly exposed server.**
>
> The certificate and its password are distributed with Basil and are therefore public. Anyone holding them can
> impersonate a server to clients that trust it.

---

## Configure the certificate

In `Configurations/appsettings.json`:

```text
Kestrel:Endpoints:Https:Url
Kestrel:Certificates:Default:Path
Kestrel:Certificates:Default:Password
```

These are the web server's own settings. For example:

```json
{
	"Kestrel": {
		"Endpoints": {
			"Https": { "Url": "https://0.0.0.0:443" }
		},
		"Certificates": {
			"Default": {
				"Path": "Configurations/basil.local.pfx",
				"Password": "basil"
			}
		}
	}
}
```

`Path` is relative to the executable unless rooted. The URL chooses the address and the port (default `443`); port `0`
picks a free port, which is useful in tests. An endpoint can have any name, and more than one can be listed.

The server reads these values once at start. **Restart Basil after changing them**; a change is logged as applying after
the restart. No rebuild is required.

For a reverse proxy that terminates TLS, give the endpoint an `http://` URL (for example `http://127.0.0.1:8080`) and
remove the certificate; Basil then serves plain HTTP. An `https://` endpoint whose certificate file is missing stops the
server at start: check the startup log and the path if Basil does not come up.

### Certificate errors

A PFX that cannot be loaded (wrong password, not a PFX, missing private key) fails the start. The startup log names the
problem and never writes the certificate password. Check the log first after changing certificate configuration.

---

## Generate a certificate for a private LAN

Replace `basil.local` with your domain. The certificate must include every name listed under
[Requirements](#requirements).

### Windows

Run **PowerShell as Administrator**:

```powershell
$domain = "basil.local"
$password = ConvertTo-SecureString -String "basil" -Force -AsPlainText

$dnsNames = @(
	$domain, "c.$domain", "ce.$domain", "c4.$domain", "c5.$domain", "c6.$domain",
	"osu.$domain", "b.$domain", "a.$domain", "api.$domain", "assets.$domain"
)

$cert = New-SelfSignedCertificate `
    -DnsName $dnsNames `
    -CertStoreLocation "cert:\LocalMachine\My" `
    -KeyExportPolicy Exportable

Export-PfxCertificate `
    -Cert "cert:\LocalMachine\My\$($cert.Thumbprint)" `
    -FilePath "basil-cert.pfx" `
    -Password $password
```

Trust the certificate on the server and on every client that connects:

```powershell
Import-PfxCertificate `
    -FilePath "basil-cert.pfx" `
    -CertStoreLocation "cert:\LocalMachine\Root" `
    -Password $password
```

Then point `Kestrel:Certificates:Default:Path` and `Password` at the PFX and restart Basil.

### macOS / Linux

```bash
domain="basil.local"
password="basil"

openssl req -new -x509 -days 365 -nodes \
  -out basil-cert.pem \
  -keyout basil-key.pem \
  -subj "/CN=$domain" \
  -addext "basicConstraints=critical,CA:TRUE" \
  -addext "subjectAltName=DNS:$domain,DNS:c.$domain,DNS:ce.$domain,DNS:c4.$domain,DNS:c5.$domain,DNS:c6.$domain,DNS:osu.$domain,DNS:b.$domain,DNS:a.$domain,DNS:api.$domain,DNS:assets.$domain"

openssl pkcs12 -export \
  -out basil-cert.pfx \
  -inkey basil-key.pem \
  -in basil-cert.pem \
  -passout "pass:$password"
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

Then configure the PFX in `Configurations/appsettings.json` and restart Basil.

---

## Reverse proxy deployments

Basil can run behind a proxy that terminates TLS:

```text
osu! client ── HTTPS ──► reverse proxy ── HTTP ──► Basil
```

The proxy owns the public certificate and must forward the original client address. Basil trusts forwarding headers
only from the addresses in `Basil:Server:TrustedProxies` (default loopback); add the proxy's address or range, or the
client's IP and country are read as the proxy's. See
[`configuration.md`](configuration.md#available-settings).

---

## Troubleshooting

### Basil does not start after changing the certificate

1. `Kestrel:Certificates:Default:Path` points at the file, relative to the executable or absolute.
2. The process can read the file.
3. `Kestrel:Certificates:Default:Password` is correct.
4. The file is a PFX containing the private key.
5. The startup log names the certificate error.

### The client reports a certificate error

1. The certificate contains the exact hostname the client uses.
2. The hostname resolves to this server.
3. The client trusts the certificate.
4. It has not expired.
5. The client connects over HTTPS to the port of `Kestrel:Endpoints:Https:Url`, normally `443`.

For LAN deployments the usual cause is a certificate that is not trusted on the client.

---

## See also

* [`configuration.md`](configuration.md) — deployment settings and data directories
* [`deployment.md`](deployment.md) — complete deployment procedure
* [`docker.md`](docker.md) — Docker deployment and certificate mounts
* [`../for-client/osu-stable/getting-started.md`](../for-client/osu-stable/getting-started.md) — client setup and trusting a LAN certificate
* [`troubleshooting.md`](troubleshooting.md) — general deployment troubleshooting
