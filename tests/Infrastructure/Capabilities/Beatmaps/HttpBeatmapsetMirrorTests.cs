using System.Net;
using Basil.Application.Capabilities.Beatmaps;
using Basil.Application.Persistence.Common;
using Basil.Application.Persistence.Content;
using Basil.Domain;
using Basil.Domain.Mechanics;
using Basil.Infrastructure.Capabilities.Beatmaps;
using Basil.Infrastructure.Capabilities.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Basil.Infrastructure.Capabilities.Tests.Beatmaps;

/// <summary>Pins the mirror search contract: what the mirror sends, and how a failure is reported.</summary>
public sealed class HttpBeatmapsetMirrorTests
{
	private const string SetJson = """
		[
			{
				"SetId": 123,
				"Artist": "Camellia",
				"Title": "Exit This Earth's Atomosphere",
				"Creator": "ProfessionalBox",
				"LastUpdate": "1700000000",
				"HasVideo": 1,
				"ChildrenBeatmaps": [
					{
						"BeatmapId": 456,
						"DiffName": "Primordial Nucleosynthesis",
						"Mode": 0,
						"DifficultyRating": 7.25
					}
				]
			}
		]
		""";

	private static readonly PageRequest Page = new(0, 50);

	/// <summary>Every field the mirror sends reaches the result under the name the contract gives it.</summary>
	[Fact]
	public async Task SearchAsync_MapsEveryFieldOfTheWireSet()
	{
		var mirror = Mirror(FakeHttpHandler.Json(SetJson), "https://mirror.test/search");

		var sets = await mirror.SearchAsync(null, null, Page, TestContext.Current.CancellationToken);

		var set = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<MirrorBeatmapset>>(sets));
		Assert.Equal(123, set.Id);
		Assert.Equal("Camellia", set.Artist);
		Assert.Equal("Exit This Earth's Atomosphere", set.Title);
		Assert.Equal("ProfessionalBox", set.Creator);
		Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), set.UpdatedAt);
		Assert.True(set.HasVideo);
		var beatmap = Assert.Single(set.Beatmaps);
		Assert.Equal(456, beatmap.Id);
		Assert.Equal("Primordial Nucleosynthesis", beatmap.Version);
		Assert.Equal(GameMode.Standard, beatmap.Mode);
		Assert.Equal(7.25, beatmap.StarRating);
	}

	/// <summary>The request asks the mirror for the requested page, text and mode.</summary>
	[Fact]
	public async Task SearchAsync_AsksForTheRequestedPageTextAndMode()
	{
		var handler = FakeHttpHandler.Json("[]");
		var mirror = Mirror(handler, "https://mirror.test/search");

		await mirror.SearchAsync("camellia exit", GameMode.Mania, new PageRequest(50, 25),
			TestContext.Current.CancellationToken);

		var request = Assert.Single(handler.Requests);
		Assert.NotNull(request.RequestUri);
		Assert.Equal("/search", request.RequestUri.AbsolutePath);
		Assert.Contains("amount=25", request.RequestUri.Query);
		Assert.Contains("offset=50", request.RequestUri.Query);
		Assert.Contains("query=camellia%20exit", request.RequestUri.Query);
		Assert.Contains("mode=3", request.RequestUri.Query);
	}

	/// <summary>A search without text or mode asks only for the page.</summary>
	[Fact]
	public async Task SearchAsync_LeavesOutTheTextAndModeItWasNotGiven()
	{
		var handler = FakeHttpHandler.Json("[]");
		var mirror = Mirror(handler, "https://mirror.test/search");

		await mirror.SearchAsync(null, null, new PageRequest(50, 25), TestContext.Current.CancellationToken);

		var request = Assert.Single(handler.Requests);
		Assert.NotNull(request.RequestUri);
		Assert.DoesNotContain("query=", request.RequestUri.Query);
		Assert.DoesNotContain("mode=", request.RequestUri.Query);
	}

	/// <summary>A mirror address that already carries parameters keeps them, and gains the request's.</summary>
	[Fact]
	public async Task SearchAsync_KeepsTheMirrorsOwnQueryParameters()
	{
		var handler = FakeHttpHandler.Json("[]");
		var mirror = Mirror(handler, "https://mirror.test/search?key=secret");

		await mirror.SearchAsync(null, null, new PageRequest(50, 25), TestContext.Current.CancellationToken);

		var request = Assert.Single(handler.Requests);
		Assert.NotNull(request.RequestUri);
		Assert.Equal("/search", request.RequestUri.AbsolutePath);
		Assert.StartsWith("?key=secret&", request.RequestUri.Query);
		Assert.Contains("amount=25", request.RequestUri.Query);
	}

	/// <summary>A set that carries no difficulties cannot be indexed, so it is left out.</summary>
	[Fact]
	public async Task SearchAsync_DropsSetsThatCarryNoDifficulties()
	{
		const string json = """
			[
				{ "SetId": 1, "LastUpdate": "1700000000", "HasVideo": 1, "ChildrenBeatmaps": null },
				{
					"SetId": 2,
					"LastUpdate": "1700000000",
					"HasVideo": 1,
					"ChildrenBeatmaps": [
						{ "BeatmapId": 3, "DiffName": "Another", "Mode": 0, "DifficultyRating": 2.0 }
					]
				}
			]
			""";
		var mirror = Mirror(FakeHttpHandler.Json(json), "https://mirror.test/search");

		var sets = await mirror.SearchAsync(null, null, Page, TestContext.Current.CancellationToken);

		var set = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<MirrorBeatmapset>>(sets));
		Assert.Equal(2, set.Id);
	}

	/// <summary>A timestamp in milliseconds is read as milliseconds, not as seconds.</summary>
	[Fact]
	public async Task SearchAsync_ReadsUnixMillisecondTimestamps()
	{
		const string json = """
			[
				{ "SetId": 5, "LastUpdate": "1700000000000", "HasVideo": 1, "ChildrenBeatmaps": [] }
			]
			""";
		var mirror = Mirror(FakeHttpHandler.Json(json), "https://mirror.test/search");

		var sets = await mirror.SearchAsync(null, null, Page, TestContext.Current.CancellationToken);

		var set = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<MirrorBeatmapset>>(sets));
		Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000), set.UpdatedAt);
	}

	/// <summary>A numeric <c>HasVideo</c> of zero means the set has no video.</summary>
	[Fact]
	public async Task SearchAsync_ReadsANumericHasVideoOfZeroAsNoVideo()
	{
		const string json = """
			[
				{ "SetId": 6, "LastUpdate": "1700000000", "HasVideo": 0, "ChildrenBeatmaps": [] }
			]
			""";
		var mirror = Mirror(FakeHttpHandler.Json(json), "https://mirror.test/search");

		var sets = await mirror.SearchAsync(null, null, Page, TestContext.Current.CancellationToken);

		var set = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<MirrorBeatmapset>>(sets));
		Assert.False(set.HasVideo);
	}

	/// <summary>Without a search mirror there is nothing to search, and no request is sent.</summary>
	[Fact]
	public async Task SearchAsync_ReturnsNullWithoutASearchMirror()
	{
		var handler = FakeHttpHandler.Json("[]");
		var mirror = Mirror(handler, searchEndpoint: null);

		var sets = await mirror.SearchAsync(null, null, Page, TestContext.Current.CancellationToken);

		Assert.Null(sets);
		Assert.Empty(handler.Requests);
	}

	/// <summary>A mirror that fails is reported as a failure, not as an error to the caller.</summary>
	[Theory]
	[InlineData(HttpStatusCode.NotFound)]
	[InlineData(HttpStatusCode.InternalServerError)]
	public async Task SearchAsync_ReturnsNullWhenTheMirrorFails(HttpStatusCode status)
	{
		var mirror = Mirror(FakeHttpHandler.Status(status), "https://mirror.test/search");

		var sets = await mirror.SearchAsync(null, null, Page, TestContext.Current.CancellationToken);

		Assert.Null(sets);
	}

	/// <summary>A body that is not the mirror's JSON is a mirror failure.</summary>
	[Fact]
	public async Task SearchAsync_ReturnsNullWhenTheBodyIsNotJson()
	{
		var mirror = Mirror(FakeHttpHandler.Json("<html>not a mirror</html>"), "https://mirror.test/search");

		var sets = await mirror.SearchAsync(null, null, Page, TestContext.Current.CancellationToken);

		Assert.Null(sets);
	}

	/// <summary>A request the mirror never answers in time is a mirror failure.</summary>
	[Fact]
	public async Task SearchAsync_ReturnsNullWhenTheRequestTimesOut()
	{
		var handler = new FakeHttpHandler((_, _) =>
			Task.FromException<HttpResponseMessage>(new TaskCanceledException("The request timed out.",
				new TimeoutException())));
		var mirror = Mirror(handler, "https://mirror.test/search");

		var sets = await mirror.SearchAsync(null, null, Page, TestContext.Current.CancellationToken);

		Assert.Null(sets);
	}

	/// <summary>The caller's cancellation belongs to the caller: it is not a mirror failure.</summary>
	[Fact]
	public async Task SearchAsync_LetsTheCallersCancellationThrough()
	{
		var handler = new FakeHttpHandler((_, cancellationToken) =>
		{
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
		});
		var mirror = Mirror(handler, "https://mirror.test/search");
		using var cancellation = new CancellationTokenSource();
		await cancellation.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			mirror.SearchAsync(null, null, Page, cancellation.Token));
	}

	/// <summary>The download address is the mirror's address for the set, with the video choice as its flag.</summary>
	[Theory]
	[InlineData(true, "https://mirror.test/d/123?n=0")]
	[InlineData(false, "https://mirror.test/d/123?n=1")]
	public async Task GetDownloadAddressAsync_BuildsTheMirrorsAddress(bool withVideo, string expected)
	{
		var mirror = Mirror(FakeHttpHandler.Json("[]"), searchEndpoint: null,
			downloadEndpoint: "https://mirror.test/d/");

		var address = await mirror.GetDownloadAddressAsync(123, withVideo, TestContext.Current.CancellationToken);

		Assert.Equal(new Uri(expected), address);
	}

	/// <summary>Without a download mirror there is nowhere to download from.</summary>
	[Fact]
	public async Task GetDownloadAddressAsync_ReturnsNullWithoutADownloadMirror()
	{
		var mirror = Mirror(FakeHttpHandler.Json("[]"), searchEndpoint: null);

		var address = await mirror.GetDownloadAddressAsync(123, false, TestContext.Current.CancellationToken);

		Assert.Null(address);
	}

	private static HttpBeatmapsetMirror Mirror(FakeHttpHandler handler, string? searchEndpoint,
		string? downloadEndpoint = null)
	{
		var settings = Substitute.For<ISettingsRepository>();
		settings.GetAsync(Arg.Any<CancellationToken>()).Returns(new ServerSettings
		{
			BeatmapMirror = new ServerSettings.BeatmapMirrorSettings
			{
				SearchEndpoint = searchEndpoint is null ? null : new Uri(searchEndpoint),
				DownloadEndpoint = downloadEndpoint is null ? null : new Uri(downloadEndpoint)
			}
		});
		return new HttpBeatmapsetMirror(settings, NullLogger<HttpBeatmapsetMirror>.Instance,
			new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan });
	}
}
