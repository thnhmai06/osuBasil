using Basil.Application.Configurations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Basil.Application.Tests.Configurations;

/// <summary>
///     Verifies every configuration section Basil actually reads binds correctly through the standard IOptions&lt;T
///     &gt; pipeline.
/// </summary>
public class OptionsBindingTests
{
	private static T BindOptions<T>(string sectionName, Dictionary<string, string?> values)
		where T : class
	{
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(values)
			.Build();

		var services = new ServiceCollection();
		services.Configure<T>(configuration.GetSection(sectionName));

		using var provider = services.BuildServiceProvider();
		return provider.GetRequiredService<IOptions<T>>().Value;
	}

	// DatabaseOptions is no longer bound from IConfiguration — fixed to Data/Basil.db.
	// No binding tests needed.

	[Fact]
	public void MirrorOptions_Binds_DownloadEndpoint()
	{
		var options = BindOptions<MirrorOptions>(MirrorOptions.SectionName, new Dictionary<string, string?>
		{
			[$"{MirrorOptions.SectionName}:DownloadEndpoint"] = "https://catboy.best/d"
		});

		Assert.Equal("https://catboy.best/d", options.DownloadEndpoint);
	}

	[Fact]
	public void MirrorOptions_DownloadEndpoint_IsNullByDefault()
	{
		var options = BindOptions<MirrorOptions>(MirrorOptions.SectionName, new Dictionary<string, string?>());

		Assert.Null(options.DownloadEndpoint);
	}

	[Fact]
	public void MirrorOptions_IsOnlineMode_FalseWhenDownloadEndpointUnset()
	{
		var options = BindOptions<MirrorOptions>(MirrorOptions.SectionName, new Dictionary<string, string?>());

		Assert.False(options.IsOnlineMode);
	}

	[Fact]
	public void MirrorOptions_IsOnlineMode_TrueWhenDownloadEndpointSet()
	{
		var options = BindOptions<MirrorOptions>(MirrorOptions.SectionName, new Dictionary<string, string?>
		{
			[$"{MirrorOptions.SectionName}:DownloadEndpoint"] = "https://catboy.best/d"
		});

		Assert.True(options.IsOnlineMode);
	}

	[Fact]
	public void MirrorOptions_HasSearchMirror_FalseWhenSearchEndpointUnset()
	{
		var options = BindOptions<MirrorOptions>(MirrorOptions.SectionName, new Dictionary<string, string?>());

		Assert.False(options.HasSearchMirror);
	}

	[Fact]
	public void MirrorOptions_HasSearchMirror_TrueWhenSearchEndpointSet()
	{
		var options = BindOptions<MirrorOptions>(MirrorOptions.SectionName, new Dictionary<string, string?>
		{
			[$"{MirrorOptions.SectionName}:SearchEndpoint"] = "https://catboy.best/api/v2/search"
		});

		Assert.True(options.HasSearchMirror);
	}

	[Fact]
	public void ServerOptions_Binds_AllFields()
	{
		var options = BindOptions<ServerOptions>(ServerOptions.SectionName,
			new Dictionary<string, string?>
			{
				[$"{ServerOptions.SectionName}:Domain"] = "akatsuki.gg"
			});

		Assert.Equal("akatsuki.gg", options.Domain);
	}

	[Fact]
	public void BotOptions_Binds_NameAndCommandPrefix()
	{
		var options = BindOptions<BotOptions>(BotOptions.SectionName, new Dictionary<string, string?>
		{
			[$"{BotOptions.SectionName}:Name"] = "TourneyBot",
			[$"{BotOptions.SectionName}:CommandPrefix"] = "!"
		});

		Assert.Equal("TourneyBot", options.Name);
		Assert.Equal("!", options.CommandPrefix);
	}
}