using System.Net;

namespace Basil.Configuration;

public static class KestrelSetup
{
	public const long DefaultMaxRequestBodySize = 1024 * 1024;

	public const string PublishedCertificatePassword = "basil";

	public static void Configure(WebApplicationBuilder builder)
	{
		builder.WebHost.ConfigureKestrel(kestrel =>
		{
			kestrel.AddServerHeader = false;
			kestrel.Limits.MaxRequestBodySize = DefaultMaxRequestBodySize;
		});
	}

	public static bool UsesHttps(IConfiguration configuration)
	{
		return Urls(configuration).Any(url => url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
	}

	public static bool UsesPublishedCertificate(IConfiguration configuration)
	{
		return UsesHttps(configuration) &&
		       configuration["Kestrel:Certificates:Default:Password"] == PublishedCertificatePassword;
	}

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