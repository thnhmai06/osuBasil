using Basil.Application.Content.Persistence;
using Basil.Application.Beatmaps.Capabilities;
using Basil.Host;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Basil.Host.Tests;

/// <summary>The Beatmaps module reads the current mirror endpoints from server settings on each request.</summary>
public sealed class BeatmapMirrorSettingsTests
{
	[Fact]
	public async Task GetAsync_ReturnsTheCurrentSearchAndDownloadEndpoints()
	{
		var current = new ServerSettings
		{
			BeatmapMirror = new ServerSettings.BeatmapMirrorSettings
			{
				SearchEndpoint = new Uri("https://first.test/search"),
				DownloadEndpoint = new Uri("https://first.test/download")
			}
		};
		var settings = Substitute.For<ISettingsRepository>();
		settings.GetAsync(Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(current));
		var services = new ServiceCollection();
		services.AddSingleton(settings);
		services.AddBeatmapMirrorSettings();
		using var provider = services.BuildServiceProvider();
		var mirrorSettings = provider.GetRequiredService<IBeatmapMirrorSettings>();

		var first = await mirrorSettings.GetAsync(TestContext.Current.CancellationToken);
		current = new ServerSettings
		{
			BeatmapMirror = new ServerSettings.BeatmapMirrorSettings
			{
				SearchEndpoint = new Uri("https://second.test/search"),
				DownloadEndpoint = new Uri("https://second.test/download")
			}
		};
		var second = await mirrorSettings.GetAsync(TestContext.Current.CancellationToken);

		Assert.Equal((new Uri("https://first.test/search"), new Uri("https://first.test/download")), first);
		Assert.Equal((new Uri("https://second.test/search"), new Uri("https://second.test/download")), second);
	}
}