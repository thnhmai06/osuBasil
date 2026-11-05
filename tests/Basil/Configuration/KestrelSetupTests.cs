using Basil.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Basil.Tests.Configuration;

/// <summary>The web server hides what it is and refuses large request bodies unless a route asks for more.</summary>
public sealed class KestrelSetupTests
{
	private static KestrelServerOptions Options()
	{
		var builder = WebApplication.CreateBuilder();
		KestrelSetup.Configure(builder);
		return builder.Build().Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value;
	}

	[Fact]
	public void TheServerHeader_IsNotSent()
	{
		Assert.False(Options().AddServerHeader);
	}

	[Fact]
	public void RequestBodies_AreSmallUnlessARouteRaisesTheLimit()
	{
		Assert.Equal(KestrelSetup.DefaultMaxRequestBodySize, Options().Limits.MaxRequestBodySize);
		Assert.True(KestrelSetup.DefaultMaxRequestBodySize <= 1024 * 1024);
	}
}