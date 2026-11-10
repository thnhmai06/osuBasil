using Basil.Application.Common;
using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Users.Persistence;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;
using Basil.Testing.Persistence;

namespace Basil.Infrastructure.Multiplayer.Tests.Persistence;

/// <summary>
///     Contract tests of who may view a private match. The rule comes from the owner's decision: a public match is
///     viewable by everyone; a private one only by a user who took part in it (a player-joined event targets the user),
///     its
///     creator, or a referee that is still one (a referee-added event not followed by a referee-removed event for the same
///     user). The privilege that lets a user view every private match is not a persistence concern.
/// </summary>
[Collection("Persistence")]
public sealed class EfMatchVisibilityTests(PersistenceFixture container) : DatabaseTests(container)
{
	private static readonly DateTimeOffset At = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

	private IMatchRepository Matches => Get<IMatchRepository>();

	private IMatchEventRepository Events => Get<IMatchEventRepository>();

	[Fact]
	public async Task Stranger_PublicOnly()
	{
		var creator = await UserAsync("creator");
		var stranger = await UserAsync("stranger");
		var publicMatch = await MatchAsync("public_match", creator, false);
		var privateMatch = await MatchAsync("private_match", creator, true);

		await AssertVisibleAsync(publicMatch, stranger, true);
		await AssertVisibleAsync(privateMatch, stranger, false);
		Assert.Equal(["public_match"], await ListedNamesAsync(new MatchQuery(VisibleTo: stranger.Id)));
	}

	[Fact]
	public async Task Creator_SeesPrivateMatch()
	{
		var creator = await UserAsync("creator");
		var match = await MatchAsync("private_match", creator, true);

		await AssertVisibleAsync(match, creator, true);
	}

	[Fact]
	public async Task Referee_SeesPrivateMatch()
	{
		var (match, referee) = await PrivateMatchWithAsync(MatchEventType.RefAdded);

		await AssertVisibleAsync(match, referee, true);
	}

	[Fact]
	public async Task JoinedPlayer_SeesPrivateMatch()
	{
		var (match, player) = await PrivateMatchWithAsync(MatchEventType.PlayerJoined);

		await AssertVisibleAsync(match, player, true);
	}

	[Fact]
	public async Task RemovedReferee_DeniedPrivateMatch()
	{
		var (match, referee) = await PrivateMatchWithAsync(MatchEventType.RefAdded, MatchEventType.RefRemoved);

		await AssertVisibleAsync(match, referee, false);
	}

	[Fact]
	public async Task ReaddedReferee_SeesPrivateMatch()
	{
		var (match, referee) = await PrivateMatchWithAsync(MatchEventType.RefAdded, MatchEventType.RefRemoved,
			MatchEventType.RefAdded);

		await AssertVisibleAsync(match, referee, true);
	}

	[Fact]
	public async Task RemovedRefereeJoined_SeesPrivateMatch()
	{
		var (match, referee) = await PrivateMatchWithAsync(MatchEventType.RefAdded, MatchEventType.RefRemoved,
			MatchEventType.PlayerJoined);

		await AssertVisibleAsync(match, referee, true);
	}

	[Fact]
	public async Task AddedReferee_StrangerStillDenied()
	{
		var creator = await UserAsync("creator");
		var referee = await UserAsync("referee");
		var stranger = await UserAsync("stranger");
		var match = await MatchAsync("private_match", creator, true);
		await RecordAsync(match, MatchEventType.RefAdded, referee);

		await AssertVisibleAsync(match, stranger, false);
	}

	[Fact]
	public async Task OtherMatchReferee_DeniedPrivate()
	{
		var creator = await UserAsync("creator");
		var referee = await UserAsync("referee");
		var refereed = await MatchAsync("refereed_match", creator, true);
		var other = await MatchAsync("other_match", creator, true);
		await RecordAsync(refereed, MatchEventType.RefAdded, referee);

		await AssertVisibleAsync(other, referee, false);
	}

	[Fact]
	public async Task PrivateMatchRequest_OnlyVisibleOnes()
	{
		var creator = await UserAsync("creator");
		var player = await UserAsync("player");
		var joined = await MatchAsync("joined_match", creator, true);
		await MatchAsync("foreign_match", creator, true);
		await MatchAsync("public_match", creator, false);
		await RecordAsync(joined, MatchEventType.PlayerJoined, player);

		var names = await ListedNamesAsync(MatchQuery.Parse("private=1") with { VisibleTo = player.Id });

		Assert.Equal(["joined_match"], names);
	}

	[Fact]
	public async Task PublicMatchRequest_OnlyPublicOnes()
	{
		var creator = await UserAsync("creator");
		await MatchAsync("own_private_match", creator, true);
		await MatchAsync("public_match", creator, false);

		var names = await ListedNamesAsync(MatchQuery.Parse("private=0") with { VisibleTo = creator.Id });

		Assert.Equal(["public_match"], names);
	}

	[Fact]
	public async Task NoViewerListing_ShowsEveryMatch()
	{
		var creator = await UserAsync("creator");
		await MatchAsync("private_match", creator, true);
		await MatchAsync("public_match", creator, false);

		var names = await ListedNamesAsync(new MatchQuery());

		Assert.Equal(2, names.Count);
	}

	/// <summary>Creates a private match and records the given events against a user in order.</summary>
	private async Task<(MatchRecord Match, UserRecord User)> PrivateMatchWithAsync(params MatchEventType[] events)
	{
		var creator = await UserAsync("creator");
		var user = await UserAsync("member");
		var match = await MatchAsync("private_match", creator, true);
		foreach (var type in events) await RecordAsync(match, type, user);
		return (match, user);
	}

	/// <summary>Asserts that the single-match check and the listing both say whether the viewer may see the match.</summary>
	private async Task AssertVisibleAsync(MatchRecord match, UserRecord viewer, bool expected)
	{
		var single = await Matches.IsVisibleToAsync(match, viewer, TestContext.Current.CancellationToken);
		var listed = (await Matches.ListAsync(new MatchQuery(VisibleTo: viewer.Id), new PageRequest(0, 50),
			TestContext.Current.CancellationToken)).Items.Any(item => item.Id == match.Id);

		Assert.Equal(expected, single);
		Assert.Equal(expected, listed);
	}

	private async Task<List<string>> ListedNamesAsync(MatchQuery query)
	{
		var page = await Matches.ListAsync(query, new PageRequest(0, 50), TestContext.Current.CancellationToken);
		return page.Items.OrderBy(match => match.Id).Select(match => match.Value.Name).ToList();
	}

	private async Task<UserRecord> UserAsync(string name)
	{
		return await Get<IUserRepository>().CreateAsync(new UserData { Name = name });
	}

	private async Task<MatchRecord> MatchAsync(string name, UserRecord creator, bool isPrivate)
	{
		return await Matches.CreateAsync(StoredMatches.Data(name, At, creator, isPrivate: isPrivate));
	}

	/// <summary>Records an event that targets a user and waits until it is stored.</summary>
	private async Task RecordAsync(MatchRecord match, MatchEventType type, UserRecord target)
	{
		var before = (await Events.ListAsync(match, TestContext.Current.CancellationToken)).Count;
		await Events.RecordAsync(StoredMatches.Event(match, type, At, target: target));
		await StoredRows.WaitForAsync(ConnectionString, "select id from match_events where match_id = @id",
			new Dictionary<string, object?> { ["@id"] = match.Id },
			rows => rows.Count == before + 1, "the recorded event", TestContext.Current.CancellationToken);
	}
}