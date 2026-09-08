using System.ComponentModel.DataAnnotations;
using Basil.Server.Shared.Configuration;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Basil.Server.Host;

/// <summary>Configures the host's Kestrel endpoint.</summary>
internal static class KestrelSetup
{
	/// <summary>
	///     Configures the Kestrel endpoint and HTTPS certificate from the <c>Basil:Server</c> section.
	/// </summary>
	/// <remarks>
	///     The section supplies <c>Port</c> (default 443), <c>CertPath</c>, and <c>CertPassword</c>;
	///     the server binds exclusively on that port. Leaving <c>CertPath</c>/<c>CertPassword</c>
	///     unset uses the dev cert or OS-level TLS. A bad cert path or password is logged at Critical
	///     (path only, never the password) before the process exits with code 1. The <c>Server</c>
	///     response header is suppressed, since advertising the exact server software is unnecessary
	///     reconnaissance information for an attacker.
	/// </remarks>
	/// <param name="builder">The web application builder whose Kestrel options are configured.</param>
	public static void Configure(WebApplicationBuilder builder)
	{
		builder.WebHost.ConfigureKestrel((context, options) =>
		{
			options.AddServerHeader = false;

			var logger = options.ApplicationServices.GetService<ILoggerFactory>()?
				.CreateLogger(typeof(Bootstrap).FullName ?? "Bootstrap");

			var serverSection = context.Configuration.GetSection(ServerOptions.SectionName);
			var domain = serverSection.GetValue<string>("Domain");
			var port = serverSection.GetValue<int?>("Port") ?? 443;
			var rawCertPath = serverSection.GetValue<string?>("CertPath");
			var certPassword = serverSection.GetValue<string?>("CertPassword");

			var certPath = !string.IsNullOrWhiteSpace(rawCertPath)
				? Path.GetFullPath(rawCertPath)
				: null;
			if (certPath is not null) logger?.LogInformation("Certificate path: {CertPath}", certPath);

			try
			{
				if (string.IsNullOrWhiteSpace(domain))
					throw new ValidationException("Domain is required.");
				if (Uri.CheckHostName(domain) != UriHostNameType.Dns)
					throw new ValidationException("Domain must be a valid DNS hostname.");
				if (string.Equals(domain, "localhost", StringComparison.OrdinalIgnoreCase))
					throw new ValidationException("'localhost' is not allowed.");
				if (!domain.Contains('.'))
					throw new ValidationException(
						"Domain must be a fully qualified domain name (for example, 'example.com').");
			}
			catch (ValidationException ex)
			{
				logger?.LogCritical(ex, "Domain is not valid");
				Environment.Exit(1);
			}

			// Lets a browser multiplex several SSE connections over one connection instead of
			// hitting HTTP/1.1's ~6-per-origin ceiling; only takes effect over TLS, which every
			// listener here already uses.
			options.ConfigureEndpointDefaults(listenOptions =>
				listenOptions.Protocols = HttpProtocols.Http1AndHttp2);

			try
			{
				options.ListenAnyIP(port, listenOptions =>
				{
					if (certPath is not null)
						listenOptions.UseHttps(certPath, certPassword);
					else
						listenOptions.UseHttps();
				});
			}
			catch (Exception ex)
			{
				logger?.LogCritical(ex, "Failed to load TLS certificate from {CertPath}.", certPath);
				Environment.Exit(1);
			}
		});
	}
}
