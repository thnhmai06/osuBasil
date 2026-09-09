using System.Net;
using System.Net.Http.Headers;
using Basil.Server.Features.Content;
using Basil.Server.Shared.Configuration;
using Basil.Server.Host;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Basil.IntegrationTests;

/// <summary>
///     Covers the `/diagnostic` endpoints end to end over TestServer's in-memory HTTP transport: the
///     admin-key gate, a plain reading, a live stream's immediate self-seeded first event, the curated
///     overview stream, and the one supported action.
/// </summary>
public class DiagnosticEndpointTests : IClassFixture<WebApplicationFactory<Bootstrap>>
{
	private const string CorrectKey = "correct-key";

	private readonly WebApplicationFactory<Bootstrap> _factory;

	public DiagnosticEndpointTests(WebApplicationFactory<Bootstrap> factory)
	{
		_factory = factory.WithWebHostBuilder(builder =>
		{
			builder.ConfigureAppConfiguration((_, config) =>
			{
				config.AddInMemoryCollection(new Dictionary<string, string?>
				{
					["Basil:Server:Domain"] = "test.local",
					["Basil:Bot:CommandPrefix"] = "!"
				});
			});
			builder.ConfigureServices(services =>
			{
				services.AddSingleton<IOptions<DatabaseOptions>>(Options.Create(new DatabaseOptions { Path = "" }));
				services.AddSingleton<ISettingsRepository>(TestDoubles.FixedAdminKeySettingsRepository(CorrectKey));
			});
		});
	}

	private static HttpRequestMessage MakeRequest(HttpMethod method, string path, string? adminKey = null)
	{
		var request = new HttpRequestMessage(method, path) { Headers = { Host = "api.test.local" } };
		if (adminKey is not null) request.Headers.Add("Authorization", $"Bearer {adminKey}");
		return request;
	}

	private static readonly string[] PlainReadingPaths =
	[
		"/diagnostic/process", "/diagnostic/gc", "/diagnostic/threadpool", "/diagnostic/runtime",
		"/diagnostic/exceptions", "/diagnostic/http", "/diagnostic/application"
	];

	private static readonly string[] LivePaths =
	[
		"/diagnostic/process/live", "/diagnostic/gc/live", "/diagnostic/threadpool/live",
		"/diagnostic/runtime/live", "/diagnostic/exceptions/live", "/diagnostic/http/live",
		"/diagnostic/application/live", "/diagnostic/live"
	];

	[Fact]
	public async Task EveryPlainReading_WithoutAdminKey_Rejected()
	{
		var client = _factory.CreateClient();

		foreach (var path in PlainReadingPaths)
		{
			var response = await client.SendAsync(MakeRequest(HttpMethod.Get, path));
			Assert.True(HttpStatusCode.Unauthorized == response.StatusCode, $"{path} did not require the admin key.");
		}
	}

	[Fact]
	public async Task EveryLiveStream_WithoutAdminKey_Rejected()
	{
		var client = _factory.CreateClient();

		foreach (var path in LivePaths)
		{
			var request = MakeRequest(HttpMethod.Get, path);
			request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
			var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
			Assert.True(HttpStatusCode.Unauthorized == response.StatusCode, $"{path} did not require the admin key.");
		}
	}

	[Fact]
	public async Task CollectGarbageAction_WithoutAdminKey_Rejected()
	{
		var client = _factory.CreateClient();

		var response = await client.SendAsync(MakeRequest(HttpMethod.Post, "/diagnostic/gc/collect", null));

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	[Fact]
	public async Task GetProcess_WithAdminKey_ReturnsAProcessReading()
	{
		var client = _factory.CreateClient();

		var response = await client.SendAsync(MakeRequest(HttpMethod.Get, "/diagnostic/process", CorrectKey));
		var body = await response.Content.ReadAsStringAsync();

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Contains("\"processId\"", body);
		Assert.Contains("\"workingSetBytes\"", body);
	}

	/// <summary>
	///     A connection to a category nobody else is watching still gets a real, current reading as
	///     its first event -- <c>DiagnosticRoutes.StreamCategory</c> seeds it itself rather than
	///     leaving the connection to wait on the periodic broadcast tick. Uses the same generous,
	///     load-tolerant bound as the rest of this codebase's SSE tests rather than a tight one: the
	///     seeding is a latency improvement in production, not something this suite can assert on
	///     without racing a real wall clock against a real background timer.
	/// </summary>
	[Fact]
	public async Task GetGcLive_FirstEventIsARealGcReading()
	{
		var client = _factory.CreateClient();
		var request = MakeRequest(HttpMethod.Get, "/diagnostic/gc/live", CorrectKey);
		request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
		using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

		var (eventType, data) = await ConnectAndReadFirstEventAsync(client, request, cancellation.Token);

		Assert.Equal("gc", eventType);
		Assert.Contains("\"gen0Collections\"", data);
	}

	[Fact]
	public async Task GetOverviewLive_FirstEventCarriesTheCuratedFields()
	{
		var client = _factory.CreateClient();
		var request = MakeRequest(HttpMethod.Get, "/diagnostic/live", CorrectKey);
		request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
		using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

		var (eventType, data) = await ConnectAndReadFirstEventAsync(client, request, cancellation.Token);

		Assert.Equal("overview", eventType);
		Assert.Contains("\"cpuUsagePercent\"", data);
		Assert.Contains("\"activeMatches\"", data);
	}

	[Fact]
	public async Task CollectGarbageAction_ReturnsAGcReadingAndCreatesNoFollowUpResource()
	{
		var client = _factory.CreateClient();

		var response = await client.SendAsync(MakeRequest(HttpMethod.Post, "/diagnostic/gc/collect", CorrectKey));
		var body = await response.Content.ReadAsStringAsync();

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Null(response.Headers.Location);
		Assert.Contains("\"gen0Collections\"", body);
	}

	[Fact]
	public async Task PostUnknownAction_ReturnsNotFound()
	{
		var client = _factory.CreateClient();

		var response =
			await client.SendAsync(MakeRequest(HttpMethod.Post, "/diagnostic/process/collect", CorrectKey));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	private static async Task<(string? EventType, string Data)> ConnectAndReadFirstEventAsync(
		HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
	{
		using var response =
			await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		response.EnsureSuccessStatusCode();
		await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
		using var reader = new StreamReader(stream);

		string? eventType = null;
		string? data = null;
		while (true)
		{
			var line = await reader.ReadLineAsync(cancellationToken);
			if (line is null) throw new IOException("Stream ended unexpectedly.");
			if (line.Length == 0)
			{
				if (data is not null) return (eventType, data);
				continue;
			}

			if (line.StartsWith("event: ", StringComparison.Ordinal)) eventType = line["event: ".Length..];
			else if (line.StartsWith("data: ", StringComparison.Ordinal)) data = line["data: ".Length..];
		}
	}
}
