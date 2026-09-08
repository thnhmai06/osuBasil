# HTTPS and Certificates

This page is the authoritative reference for TLS on Basil. If this document conflicts with another document, **this
document takes precedence**.

## Requirements

osu! stable requires HTTPS on port `443`. Plain HTTP connections are rejected before they reach Basil.

The TLS certificate must cover the Basil domain and all required service subdomains:

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

The certificate must therefore contain these names as SANs (Subject Alternative Names).

A certificate for only `localhost` or only the main domain is **not sufficient**.

### Public deployment

For a public domain, use a certificate issued by a trusted certificate authority.

A wildcard certificate can be used if it covers both:

```text
<domain>
*.<domain>
```

The certificate must be trusted by the client machines without installing a custom CA certificate.

### LAN-only deployment

For a private LAN deployment where the domain is not publicly resolvable, you can use a self-signed certificate (which
shipped with the build).

The certificate must still contain SANs for every Basil hostname listed above.

Because a self-signed certificate is not trusted by default, install the certificate or its issuing CA into the trusted
certificate store on every client machine that connects to Basil.

See [`getting-started.md`](../for-client/bancho/getting-started.md).

---

## IRC

**IRC does not require HTTPS.** Basil's IRC gateway is separate from the HTTPS server.

IRC uses plain TCP on:

```text
Basil:Irc:Port
```

The default port is:

```text
6667
```

IRC **does not** require:

* an `irc.<domain>` subdomain;
* an IRC-specific TLS certificate;
* HTTPS configuration.

IRC clients connect directly to the **Basil server's hostname (`<domain>`) or IP address** and the configured IRC port.

---

## Bundled development certificate

Basil ships with a self-signed certificate for local development and private LAN testing:

```text
Data/basil.local.pfx
```

The shipped certificate covers the required Basil subdomains with:
- Domain: `basil.local`
- Password: `your-password`

The shipped [`appsettings.json`](../../src/Basil.Web/Data/appsettings.json) is already configured to use this certificate.

This means a fresh local installation can start with HTTPS without generating a certificate first.

> [!IMPORTANT]
> **Do not use the bundled certificate for production or a publicly exposed server.**
>
> The certificate and its password are distributed with Basil and are therefore public. Anyone with the certificate and
private key can impersonate a server to clients that trust it.
>
> Use a certificate issued for your own domain for production deployments.

---

## Configure the certificate

Configure the certificate in `appsettings.json`:

```text
Basil:Server:CertPath
Basil:Server:CertPassword
```

For example:

```json
{
	"Basil": {
		"Server": {
			"CertPath": "Data/basil.local.pfx",
			"CertPassword": "your-password"
		}
	}
}
```

With:

- `CertPath` is the path to the PFX certificate file available to Basil.
- `CertPassword` is the password protecting the PFX file.

After changing either value, **restart Basil**. No rebuild is required.

The HTTPS port is controlled by:

```text
Basil:Server:Port
```

The default is `443`.

You do not need to pass `--urls` when starting Basil.

### Certificate errors

If Basil cannot load the configured certificate because the path or password is invalid:

* Basil logs the certificate error at `Critical` level;
* the certificate password is never written to the log;
* the process exits with code `1`.

Check the startup log first when Basil fails to start after changing certificate configuration.

---

## Generate a certificate for a private LAN

Use this procedure when you need a self-signed certificate for a private deployment.

Replace `basil.local` with the domain used by your server.

### Windows

1. Run **PowerShell as Administrator**:

```powershell
$domain = "basil.local"
$password = ConvertTo-SecureString `
    -String "your-password" `
    -Force `
    -AsPlainText

$dnsNames = @(
	$domain,
	"c.$domain",
	"ce.$domain",
	"c4.$domain",
	"c5.$domain",
	"c6.$domain",
	"osu.$domain",
	"b.$domain",
	"a.$domain",
	"api.$domain",
	"assets.$domain"
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

2. Import the certificate into the trusted root store on the server:

```powershell
Import-PfxCertificate `
    -FilePath "basil-cert.pfx" `
    -CertStoreLocation "cert:\LocalMachine\Root" `
    -Password $password
```

The same certificate must also be trusted by every client machine that connects to the server.

3. Finally, configure Basil to use the generated PFX:

```text
Basil:Server:CertPath
Basil:Server:CertPassword
```

and restart Basil.

### macOS / Linux

1. Generate the certificate and private key:

```bash
domain="basil.local"
password="your-password"

openssl req -new -x509 -days 365 -nodes \
  -out basil-cert.pem \
  -keyout basil-key.pem \
  -subj "/CN=$domain" \
  -addext "subjectAltName=DNS:$domain,DNS:c.$domain,DNS:ce.$domain,DNS:c4.$domain,DNS:c5.$domain,DNS:c6.$domain,DNS:osu.$domain,DNS:b.$domain,DNS:a.$domain,DNS:api.$domain,DNS:assets.$domain"

openssl pkcs12 -export \
  -out basil-cert.pfx \
  -inkey basil-key.pem \
  -in basil-cert.pem \
  -passout "pass:$password"
```

2. Trust the certificate:

#### Linux:

Install the certificate into the distribution's trusted certificate store.

The certificate must also be trusted on every client machine.

#### MacOS:

```bash
security add-trusted-cert \
  -d \
  -r trustRoot \
  -k ~/Library/Keychains/login.keychain \
  basil-cert.pem
```

3. Finally, configure Basil to use the generated PFX:

```text
Basil:Server:CertPath
Basil:Server:CertPassword
```

and restart Basil.

---

## Reverse proxy deployments

Basil can also run behind a reverse proxy that terminates TLS.

In this setup:

```text
osu! client
    │
    │ HTTPS
    ▼
Reverse proxy
    │
    │ HTTP/HTTPS
    ▼
Basil
```

The reverse proxy is responsible for the public TLS certificate.

Follow the reverse proxy's documentation for certificate configuration and forwarding the original client address.

If Basil is exposed directly without a reverse proxy, it handles the client connection itself.

> [!IMPORTANT]
> For production deployments, make sure Basil receives the original client IP through the expected proxy headers when a
reverse proxy is used. Incorrect proxy configuration can cause client IP and geolocation handling to fail.

---

## Troubleshooting

### Basil does not start after changing the certificate

Check:

1. `Basil:Server:CertPath` points to the correct file.
2. The certificate file is accessible by the Basil process.
3. `Basil:Server:CertPassword` is correct.
4. The certificate is a valid PFX containing the private key.
5. The startup log for the `Critical` certificate error.

### The client reports a certificate error

Check:

1. The certificate contains the exact hostname the client is connecting to.
2. The hostname resolves to the correct server.
3. The certificate is trusted by the client.
4. The certificate has not expired.
5. The client is connecting over HTTPS on port `443`.

For LAN deployments using a self-signed certificate, the most common cause is that the certificate has not been
installed as trusted on the client.

---

## See also

* [`configuration.md`](configuration.md): `Server:CertPath`, `Server:CertPassword`, and server data
* [`deployment.md`](deployment.md): complete deployment procedure
* [`docker.md`](docker.md): Docker deployment and certificate mounts
* [`getting-started.md`](../for-client/bancho/getting-started.md): configuring an osu! client and trusting a LAN certificate
* [`troubleshooting.md`](troubleshooting.md): general deployment troubleshooting
