using System.Net;

namespace Basil.Host.Configuration;

/// <summary>
///     Applies the server's limits to Kestrel, which listens where its own <c>Kestrel:Endpoints</c> settings say and
///     uses the certificate of <c>Kestrel:Certificates:Default</c> for its HTTPS endpoints.
/// </summary>
public static class KestrelSetup
{
	/// <summary>The largest request body in bytes; the routes that accept an uploaded file raise it for themselves.</summary>
	public const long DefaultMaxRequestBodySize = 1024 * 1024;

	/// <summary>The password of the certificate that ships with the server; anyone can read it in the repository.</summary>
	public const string PublishedCertificatePassword = "basil";

	/// <summary>Hides the server header and limits the size of request bodies.</summary>
	/// <param name="builder">The builder.</param>
	public static void Configure(WebApplicationBuilder builder)
	{
		builder.WebHost.ConfigureKestrel(kestrel =>
		{
			kestrel.AddServerHeader = false;
			kestrel.Limits.MaxRequestBodySize = DefaultMaxRequestBodySize;
		});
	}

	/// <summary>Tells whether an endpoint of the configuration serves HTTPS.</summary>
	/// <param name="configuration">The configuration.</param>
	public static bool UsesHttps(IConfiguration configuration)
	{
		return Urls(configuration).Any(url => url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>Tells whether the server serves HTTPS with the certificate that ships with it, whose password is public.</summary>
	/// <param name="configuration">The configuration.</param>
	public static bool UsesPublishedCertificate(IConfiguration configuration)
	{
		return UsesHttps(configuration) &&
		       configuration["Kestrel:Certificates:Default:Password"] == PublishedCertificatePassword;
	}

	/// <summary>Gives the TCP addresses and ports the configured endpoints listen on; a port of 0 is left out.</summary>
	/// <param name="configuration">The configuration.</param>
	public static IReadOnlyList<IPEndPoint> Bindings(IConfiguration configuration)
	{
		var bindings = new List<IPEndPoint>();
		foreach (var url in Urls(configuration))
		{
			var authority = url[(url.IndexOf("://", StringComparison.Ordinal) + 3)..].TrimEnd('/');
			var colon = authority.LastIndexOf(':');
			if (colon < 0 || !int.TryParse(authority[(colon + 1)..], out var port) || port == 0) continue;

			var host = authority[..colon].Trim('[', ']');
			bindings.Add(new IPEndPoint(IPAddress.TryParse(host, out var address) ? address : IPAddress.Any, port));
		}

		return bindings;
	}

	private static IEnumerable<string> Urls(IConfiguration configuration)
	{
		return configuration.GetSection("Kestrel:Endpoints").GetChildren()
			.Select(endpoint => endpoint["Url"])
			.Where(url => !string.IsNullOrEmpty(url) && url.Contains("://", StringComparison.Ordinal))!;
	}
}