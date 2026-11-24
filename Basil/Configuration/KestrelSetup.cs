namespace Basil.Configuration;

public static class KestrelSetup
{
	public const long DefaultMaxRequestBodySize = 1024 * 1024;

	public static void Configure(WebApplicationBuilder builder)
	{
		builder.WebHost.ConfigureKestrel(kestrel =>
		{
			kestrel.AddServerHeader = false;
			kestrel.Limits.MaxRequestBodySize = DefaultMaxRequestBodySize;
		});
	}
}
