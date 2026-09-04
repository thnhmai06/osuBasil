using Basil.Application.Abstractions.Settings;
using Basil.Application.Configurations;
using Basil.Application.Services.Beatmaps;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Basil.Application.Tests.Services.Beatmaps;

public class MirrorServiceTests
{
	private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
	private MirrorOptions _configSeed = new();

	private MirrorService MakeService()
	{
		return new MirrorService(_settings, Options.Create(_configSeed), NullLogger<MirrorService>.Instance);
	}

	[Fact]
	public async Task GetAsync_BothUnset_ReturnsNullEndpoints()
	{
		var result = await MakeService().GetAsync();

		Assert.Null(result.DownloadEndpoint);
		Assert.Null(result.SearchEndpoint);
		Assert.False(result.IsOnlineMode);
		Assert.False(result.HasSearchMirror);
	}

	[Fact]
	public async Task GetAsync_BothSet_ReturnsStoredValues()
	{
		_settings.GetAsync("Mirror:DownloadEndpoint", Arg.Any<CancellationToken>()).Returns("https://mirror.local/d");
		_settings.GetAsync("Mirror:SearchEndpoint", Arg.Any<CancellationToken>()).Returns("https://mirror.local/s");

		var result = await MakeService().GetAsync();

		Assert.Equal("https://mirror.local/d", result.DownloadEndpoint);
		Assert.Equal("https://mirror.local/s", result.SearchEndpoint);
		Assert.True(result.IsOnlineMode);
		Assert.True(result.HasSearchMirror);
	}

	[Fact]
	public async Task SetAsync_WritesBothKeys()
	{
		await MakeService().SetAsync("https://mirror.local/d", "https://mirror.local/s");

		await _settings.Received(1)
			.SetAsync("Mirror:DownloadEndpoint", "https://mirror.local/d", Arg.Any<CancellationToken>());
		await _settings.Received(1)
			.SetAsync("Mirror:SearchEndpoint", "https://mirror.local/s", Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task SetAsync_NullOrEmpty_ClearsThatEndpoint()
	{
		await MakeService().SetAsync(null, "");

		await _settings.Received(1).SetAsync("Mirror:DownloadEndpoint", null, Arg.Any<CancellationToken>());
		await _settings.Received(1).SetAsync("Mirror:SearchEndpoint", null, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task SeedFromConfigIfUnsetAsync_NeverSeededAndConfigHasValue_SeedsFromConfigAndMarksSeeded()
	{
		_configSeed = new MirrorOptions { DownloadEndpoint = "https://config.local/d" };

		await MakeService().SeedFromConfigIfUnsetAsync();

		await _settings.Received(1)
			.SetAsync("Mirror:DownloadEndpoint", "https://config.local/d", Arg.Any<CancellationToken>());
		await _settings.Received(1).SetAsync("Mirror:Seeded", "true", Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task SeedFromConfigIfUnsetAsync_NeverSeededAndConfigEmpty_WritesNoEndpointButMarksSeeded()
	{
		await MakeService().SeedFromConfigIfUnsetAsync();

		await _settings.DidNotReceive()
			.SetAsync("Mirror:DownloadEndpoint", Arg.Any<string?>(), Arg.Any<CancellationToken>());
		await _settings.DidNotReceive()
			.SetAsync("Mirror:SearchEndpoint", Arg.Any<string?>(), Arg.Any<CancellationToken>());
		await _settings.Received(1).SetAsync("Mirror:Seeded", "true", Arg.Any<CancellationToken>());
	}

	/// <summary>
	///     Regression test: seeding used to be gated on "is an endpoint currently unset", which meant an
	///     operator's own deliberate clear (both endpoints set back to null) looked identical to "never
	///     seeded" and the old config value would silently reappear on the next restart. Gating on a
	///     dedicated marker instead means a clear stays cleared.
	/// </summary>
	[Fact]
	public async Task SeedFromConfigIfUnsetAsync_AlreadySeeded_DoesNotReseedEvenIfEndpointsAreNowCleared()
	{
		_settings.GetAsync("Mirror:Seeded", Arg.Any<CancellationToken>()).Returns("true");
		_settings.GetAsync("Mirror:DownloadEndpoint", Arg.Any<CancellationToken>()).Returns((string?)null);
		_configSeed = new MirrorOptions { DownloadEndpoint = "https://config.local/d" };

		await MakeService().SeedFromConfigIfUnsetAsync();

		await _settings.DidNotReceive()
			.SetAsync("Mirror:DownloadEndpoint", Arg.Any<string?>(), Arg.Any<CancellationToken>());
		await _settings.DidNotReceive()
			.SetAsync("Mirror:Seeded", Arg.Any<string?>(), Arg.Any<CancellationToken>());
	}
}