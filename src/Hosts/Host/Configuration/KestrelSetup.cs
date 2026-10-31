using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace Basil.Host.Configuration;

/// <summary>Makes Kestrel listen where the configuration says.</summary>
public static class KestrelSetup
{
	/// <summary>
	///     Listens on <c>Server.Bind</c> and <c>Server.Ports.Https</c>, with TLS when a certificate is configured and plain
	///     HTTP otherwise (behind a reverse proxy). Addresses given by <c>--urls</c> or <c>ASPNETCORE_URLS</c> win.
	/// </summary>
	/// <param name="builder">The builder.</param>
	public static void Configure(WebApplicationBuilder builder)
	{
		if (!string.IsNullOrEmpty(builder.Configuration[WebHostDefaults.ServerUrlsKey])) return;

		var server = builder.Configuration.GetSection("Basil:Server").Get<ServerOptions>() ?? new ServerOptions();
		var certificate = Load(server.Certificate);
		builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Parse(server.Bind), server.Ports.Https,
			listen =>
			{
				if (certificate is not null) listen.UseHttps(certificate);
			}));
	}

	private static X509Certificate2? Load(ServerOptions.CertificateOptions options)
	{
		if (string.IsNullOrEmpty(options.Path)) return null;

		var path = ConfigurationSetup.Resolve(options.Path);
		return File.Exists(path) ? X509CertificateLoader.LoadPkcs12FromFile(path, options.Password) : null;
	}
}