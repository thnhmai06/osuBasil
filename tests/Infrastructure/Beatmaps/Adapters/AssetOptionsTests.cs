using Basil.Infrastructure.Beatmaps;
using Basil.Infrastructure.Beatmaps.Adapters;
using Basil.Infrastructure.Common.Storage;
using Basil.Testing.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Beatmaps.Tests.Adapters;

/// <summary>Where the audio previews are built: the configured directory, or one under the server's cache.</summary>
[Collection("Persistence")]
public sealed class AssetOptionsTests(PersistenceFixture container) : DatabaseTests(container)
{
	[Fact]
	public void Without_configuration_the_preview_directory_is_under_the_cache_of_the_storage_location()
	{
		var directory = Get<IOptions<AssetOptions>>().Value.CacheDirectory;

		Assert.False(string.IsNullOrWhiteSpace(directory));
		Assert.StartsWith(Get<DataPaths>().Cache.Path, directory);
	}

	[Fact]
	public void The_assets_section_of_the_configuration_sets_the_directory_and_the_ffmpeg_folder()
	{
		var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
		{
			["Basil:Assets:CacheDirectory"] = "/previews",
			["Basil:Assets:FfmpegFolder"] = "/ffmpeg"
		}).Build();
		using var provider = new ServiceCollection().AddBeatmapsInfrastructureOptions(configuration)
			.BuildServiceProvider();

		var options = provider.GetRequiredService<IOptions<AssetOptions>>().Value;

		Assert.Equal("/previews", options.CacheDirectory);
		Assert.Equal("/ffmpeg", options.FfmpegFolder);
	}
}
