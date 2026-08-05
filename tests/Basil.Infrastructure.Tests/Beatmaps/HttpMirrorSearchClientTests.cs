using System.Net;
using System.Text;
using Basil.Infrastructure.Beatmaps;
using Microsoft.Extensions.Logging.Abstractions;

namespace Basil.Infrastructure.Tests.Beatmaps;

public class HttpMirrorSearchClientTests
{
	private static HttpMirrorSearchClient MakeClient(HttpStatusCode statusCode, string body)
	{
		var handler = new StubHandler(statusCode, body);
		var httpClient = new HttpClient(handler);
		return new HttpMirrorSearchClient(httpClient, NullLogger<HttpMirrorSearchClient>.Instance);
	}

	[Fact]
	public async Task SearchAsync_NonSuccessStatusCode_ReturnsNull()
	{
		var client = MakeClient(HttpStatusCode.InternalServerError, "");

		var result = await client.SearchAsync("https://mirror.local/search", null, null, 100, 0);

		Assert.Null(result);
	}

	[Fact]
	public async Task SearchAsync_ValidPayload_MapsFieldsCorrectly()
	{
		const string json = """
		                    [{
		                        "Artist": "Camellia", "Title": "Exit This Earth's Atmosphere", "Creator": "RLC",
		                        "RankedStatus": 4, "LastUpdate": "2020-01-01 00:00:00", "SetID": 321,
		                        "HasVideo": true,
		                        "ChildrenBeatmaps": [
		                            {"DifficultyRating": 6.42, "DiffName": "Extreme", "CS": 4, "OD": 8, "AR": 9, "HP": 6, "Mode": 0}
		                        ]
		                    }]
		                    """;
		var client = MakeClient(HttpStatusCode.OK, json);

		var result = await client.SearchAsync("https://mirror.local/search", null, null, 100, 0);

		Assert.NotNull(result);
		var set = Assert.Single(result);
		Assert.Equal("Camellia", set.Artist);
		Assert.Equal(321, set.SetId);
		Assert.True(set.HasVideo);
		var diff = Assert.Single(set.Beatmaps!);
		Assert.Equal("Extreme", diff.DiffName);
		Assert.Equal(6.42, diff.DifficultyRating);
	}

	[Fact]
	public async Task SearchAsync_HasVideoAsIntegerOne_ParsesAsTrue()
	{
		const string json = """
		                    [{
		                        "Artist": "A", "Title": "T", "Creator": "C", "RankedStatus": 4,
		                        "LastUpdate": "2020-01-01 00:00:00", "SetID": 1, "HasVideo": 1,
		                        "ChildrenBeatmaps": []
		                    }]
		                    """;
		var client = MakeClient(HttpStatusCode.OK, json);

		var result = await client.SearchAsync("https://mirror.local/search", null, null, 100, 0);

		Assert.True(result!.Single().HasVideo);
	}

	[Fact]
	public async Task SearchAsync_HasVideoAsIntegerZero_ParsesAsFalse()
	{
		const string json = """
		                    [{
		                        "Artist": "A", "Title": "T", "Creator": "C", "RankedStatus": 4,
		                        "LastUpdate": "2020-01-01 00:00:00", "SetID": 1, "HasVideo": 0,
		                        "ChildrenBeatmaps": []
		                    }]
		                    """;
		var client = MakeClient(HttpStatusCode.OK, json);

		var result = await client.SearchAsync("https://mirror.local/search", null, null, 100, 0);

		Assert.False(result!.Single().HasVideo);
	}

	[Fact]
	public async Task SearchAsync_SetWithNullChildrenBeatmaps_ExcludedFromResults()
	{
		const string json = """
		                    [
		                        {"Artist": "A", "Title": "T", "Creator": "C", "RankedStatus": 4, "LastUpdate": "2020-01-01 00:00:00", "SetID": 1, "HasVideo": false, "ChildrenBeatmaps": null},
		                        {"Artist": "B", "Title": "T2", "Creator": "C2", "RankedStatus": 4, "LastUpdate": "2020-01-01 00:00:00", "SetID": 2, "HasVideo": false, "ChildrenBeatmaps": []}
		                    ]
		                    """;
		var client = MakeClient(HttpStatusCode.OK, json);

		var result = await client.SearchAsync("https://mirror.local/search", null, null, 100, 0);

		Assert.NotNull(result);
		Assert.Equal(2, Assert.Single(result).SetId);
	}

	private sealed class StubHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			return Task.FromResult(new HttpResponseMessage(statusCode)
			{
				Content = new StringContent(body, Encoding.UTF8, "application/json")
			});
		}
	}
}