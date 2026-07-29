using System.Net;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Abstractions.Scores;
using Basil.Application.Configuration;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Basil.IntegrationTests;

/// <summary>Covers the read-only slice of the api. host's TRT endpoint, GET /matches/{matchId}.</summary>
public class MatchReportEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
	private readonly WebApplicationFactory<Program> _factory;
	private MatchRow? _match;

	public MatchReportEndpointTests(WebApplicationFactory<Program> factory)
	{
		var matchPersistence = Substitute.For<IMatchPersistenceRepository>();
		// Never exercised by this read-only report suite -- throw, matching the old fake, instead of
		// the NSubstitute default of a silently-completed task.
		matchPersistence.CreateMatchAsync(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
			.Throws(new NotSupportedException());
		matchPersistence.SetMatchEndedAsync(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
			.Throws(new NotSupportedException());
		matchPersistence.CreateRoundAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<GameMode>(),
				Arg.Any<MatchWinCondition>(), Arg.Any<MatchTeamType>(), Arg.Any<Mods>(), Arg.Any<DateTime>(),
				Arg.Any<CancellationToken>())
			.Throws(new NotSupportedException());
		matchPersistence.SetRoundEndedAsync(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<bool>(),
				Arg.Any<CancellationToken>())
			.Throws(new NotSupportedException());
		matchPersistence.FetchMatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(call => _match?.Id == call.ArgAt<int>(0) ? _match : null);
		matchPersistence.FetchRoundsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<IReadOnlyList<RoundRow>>([]));
		matchPersistence.FetchAllMatchesAsync(Arg.Any<CancellationToken>())
			.Returns(_ => (IReadOnlyList<MatchRow>)(_match is null ? [] : [_match]));
		matchPersistence.FetchEventsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<IReadOnlyList<MatchEventRow>>([]));
		matchPersistence.FetchUnrecoveredMatchesAsync(Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<IReadOnlyList<MatchRow>>([]));
		matchPersistence.FetchUnrecoveredRoundsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<IReadOnlyList<RoundRow>>([]));

		var scores = Substitute.For<IScoreRepository>();
		// Never exercised by this read-only report suite -- throw, matching the old fake.
		scores.CreateAsync(Arg.Any<ScoreInsertRow>(), Arg.Any<CancellationToken>())
			.Throws(new NotSupportedException());
		scores.ExistsByOnlineChecksumAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Throws(new NotSupportedException());
		scores.FetchFirstPlaceScoreAsync(Arg.Any<string>(), Arg.Any<GameMode>(), Arg.Any<CancellationToken>())
			.Throws(new NotSupportedException());
		scores.FetchOwnerAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
			.Throws(new NotSupportedException());
		scores.FetchByIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
			.Throws(new NotSupportedException());
		scores.FetchPageAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Throws(new NotSupportedException());
		scores.FetchCountAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(0));
		scores.FetchByRoundIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<IReadOnlyList<RoundScoreRow>>([]));

		var matchRegistry = Substitute.For<IMatchRegistry>();
		matchRegistry.All.Returns((IReadOnlyList<MatchSession>)[]);

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
				services.AddSingleton(matchPersistence);
				services.AddSingleton(scores);
				services.AddSingleton(matchRegistry);
				services.AddSingleton(TestDoubles.NullUserRepository());
			});
		});
	}

	private static HttpRequestMessage MakeRequest(string path, string host = "api.test.local")
	{
		return new HttpRequestMessage(HttpMethod.Get, path) { Headers = { Host = host } };
	}

	[Fact]
	public async Task GetMulti_UnknownMatch_ReturnsNotFound()
	{
		var client = _factory.CreateClient();

		var response = await client.SendAsync(MakeRequest("/matches/999"));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task GetMulti_KnownMatch_ReturnsReportJson()
	{
		_match = new MatchRow(5, "Grand Finals",
			new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), null);
		var client = _factory.CreateClient();

		var response = await client.SendAsync(MakeRequest("/matches/5"));
		var body = await response.Content.ReadAsStringAsync();

		response.EnsureSuccessStatusCode();
		Assert.Contains("\"Grand Finals\"", body);
		Assert.Contains("\"matchId\":5", body, StringComparison.OrdinalIgnoreCase);
	}
}