using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Bot;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configurations;
using Basil.Application.Services.Bot;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Irc;
using Basil.Application.Sessions.Multiplayer;
using Basil.Application.Tests.Packets;
using Basil.Domain.Login;
using Basil.Domain.Users;
using Basil.Protocol.Irc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Basil.Application.Tests.Services.Bot;

public class CommandDispatcherTests
{
	private readonly IBeatmapRepository _beatmaps = Substitute.For<IBeatmapRepository>();
	private readonly IUserRepository _users = Substitute.For<IUserRepository>();

	/// <summary>
	///     Dispatches a chat message and returns the last reply sent; a non-null
	///     <paramref name="matchScope" /> is inferred to mean the message was sent in that match's own
	///     chat channel (see <see cref="ICommandDispatcher.DispatchAsync" />'s doc comment on matchScope).
	/// </summary>
	private static async Task<string?> Run(CommandDispatcher dispatcher, UserSession sender, string message,
		MatchSession? matchScope, bool prefixOptional = false)
	{
		var sink = new RecordingReplySink();
		await dispatcher.DispatchAsync(sender, message, matchScope, matchScope?.ChatChannelName, sink, prefixOptional);
		return sink.Last;
	}

	/// <summary>
	///     Like <see cref="Run" />, but returns every reply sent (in order) instead of just the last —
	///     needed for a `;`-chained line, where each segment sends its own reply immediately.
	/// </summary>
	private static async Task<IReadOnlyList<string>> RunAll(CommandDispatcher dispatcher, UserSession sender,
		string message, MatchSession? matchScope)
	{
		var sink = new RecordingReplySink();
		await dispatcher.DispatchAsync(sender, message, matchScope, matchScope?.ChatChannelName, sink);
		return sink.Replies;
	}

	private static StorageOptions MakeStorageOptions(string faqsPath = "")
	{
		return new StorageOptions
		{
			ReplaysPath = "", AvatarsPath = "", MapsetsPath = "", MenuSeasonalsPath = "", MenuBannersPath = "", FaqsPath = faqsPath,
			CachePath = ""
		};
	}

	private CommandDispatcher MakeDispatcher(string prefix = "!", MultiplayerTestSupport.Fixture? fixture = null,
		StorageOptions? storageOptions = null)
	{
		var options = Options.Create(new BotOptions { CommandPrefix = prefix });
		fixture ??= new MultiplayerTestSupport.Fixture();
		var mpCommands = new MpCommandService(fixture.MatchMembership, fixture.MatchRegistry, fixture.MatchRepository,
			_beatmaps,
			fixture.SessionRegistry, fixture.IrcSessionRegistry, Substitute.For<IUserRepository>(),
			fixture.ChannelRegistry,
			NullLogger<MpCommandService>.Instance,
			NullLogger<MatchControlService>.Instance);
		return new CommandDispatcher(options, mpCommands, _users,
			Options.Create(storageOptions ?? MakeStorageOptions()),
			fixture.MatchRegistry, fixture.SessionRegistry, fixture.ChannelRegistry, fixture.ChannelMembership,
			NullLogger<CommandDispatcher>.Instance);
	}

	[Fact]
	public async Task DispatchAsync_NoPrefix_ReturnsNull()
	{
		var dispatcher = MakeDispatcher();
		var sender = MultiplayerTestSupport.MakePlayer(1, "cmyui");

		var reply = await Run(dispatcher, sender, "hello there", null);

		Assert.Null(reply);
	}

	[Fact]
	public async Task DispatchAsync_NoPrefixButPrefixOptional_TreatsMessageAsCommand()
	{
		var dispatcher = MakeDispatcher();
		var sender = MultiplayerTestSupport.MakePlayer(1, "cmyui");

		var reply = await Run(dispatcher, sender, "roll", null, true);

		Assert.NotNull(reply);
		Assert.Matches(string.Format(MpReplies.RollResult, "cmyui", @"\d+").Replace("(", @"\(").Replace(")", @"\)"),
			reply);
	}

	[Fact]
	public async Task DispatchAsync_UnknownCommand_ReturnsNull()
	{
		var dispatcher = MakeDispatcher();
		var sender = MultiplayerTestSupport.MakePlayer(1, "cmyui");

		var reply = await Run(dispatcher, sender, "!bogus", null);

		Assert.Null(reply);
	}

	[Fact]
	public async Task DispatchAsync_Help_ReturnsHelpText()
	{
		var dispatcher = MakeDispatcher();
		var sender = MultiplayerTestSupport.MakePlayer(1, "cmyui");

		var reply = await Run(dispatcher, sender, "!help", null);

		Assert.NotNull(reply);
	}

	[Fact]
	public async Task DispatchAsync_RollNoArg_DefaultsMaxTo100()
	{
		var dispatcher = MakeDispatcher();
		var sender = MultiplayerTestSupport.MakePlayer(1, "cmyui");

		var reply = await Run(dispatcher, sender, "!roll", null);

		Assert.NotNull(reply);
		Assert.Matches(string.Format(MpReplies.RollResult, "cmyui", @"\d+").Replace("(", @"\(").Replace(")", @"\)"),
			reply);
		var pointsToken = reply.Split(' ')[2];
		var points = int.Parse(pointsToken);
		Assert.InRange(points, 0, 100);
	}

	[Fact]
	public async Task DispatchAsync_RollLargeArg_StaysWithinRequestedMax()
	{
		var dispatcher = MakeDispatcher();
		var sender = MultiplayerTestSupport.MakePlayer(1, "cmyui");

		var reply = await Run(dispatcher, sender, "!roll 999999", null);

		var points = int.Parse(reply!.Split(' ')[2]);
		Assert.InRange(points, 0, 999999);
	}

	[Fact]
	public async Task DispatchAsync_RollAtIntMaxValue_DoesNotThrow()
	{
		var dispatcher = MakeDispatcher();
		var sender = MultiplayerTestSupport.MakePlayer(1, "cmyui");

		var reply = await Run(dispatcher, sender, "!roll 2147483647", null);

		Assert.NotNull(reply);
		var points = int.Parse(reply.Split(' ')[2]);
		Assert.InRange(points, 0, int.MaxValue);
	}

	[Fact]
	public async Task DispatchAsync_MpWithoutMatchScope_RepliesNotScoped()
	{
		var dispatcher = MakeDispatcher();
		var sender = MultiplayerTestSupport.MakePlayer(1, "cmyui");

		var reply = await Run(dispatcher, sender, "!mp settings", null);

		Assert.Contains(MpReplies.NotScopedToAnyMatchHint, reply);
	}

	[Fact]
	public async Task DispatchAsync_MpWithMatchScope_RoutesToMpCommandService()
	{
		var dispatcher = MakeDispatcher();
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var fixture = new MultiplayerTestSupport.Fixture();
		fixture.RegisterAll(host);
		var match = fixture.CreateMatch(host);

		var reply = await Run(dispatcher, host, "!mp help", match);

		Assert.NotNull(reply);
		Assert.Contains("settings", reply);
	}

	[Fact]
	public async Task DispatchAsync_MpMakeWithoutMatchScope_Succeeds()
	{
		var fixture = new MultiplayerTestSupport.Fixture();
		var dispatcher = MakeDispatcher(fixture: fixture);
		var sender = MultiplayerTestSupport.MakePlayer(1, "creator");
		fixture.RegisterAll(sender);

		var reply = await Run(dispatcher, sender, "!mp make Room", null);

		Assert.NotNull(reply);
		Assert.NotNull(sender.Match);
		Assert.Contains(string.Format(MpReplies.CreatedMatch, sender.Match.DbId, sender.Match.Name, ""), reply);
	}

	[Fact]
	public async Task DispatchAsync_MpMakeprivateWithoutMatchScope_CreatesPrivateMatch()
	{
		var fixture = new MultiplayerTestSupport.Fixture();
		var dispatcher = MakeDispatcher(fixture: fixture);
		var sender = MultiplayerTestSupport.MakePlayer(1, "creator");
		fixture.RegisterAll(sender);

		var reply = await Run(dispatcher, sender, "!mp makeprivate Room", null);

		Assert.NotNull(reply);
		Assert.NotNull(sender.Match);
		Assert.Contains(string.Format(MpReplies.CreatedMatch, sender.Match.DbId, sender.Match.Name, " (private)"),
			reply);
		Assert.True(sender.Match.IsPrivate);
	}

	[Fact]
	public async Task DispatchAsync_MpMakeprivateWithMatchScope_CreatesNewPrivateMatchIgnoringScope()
	{
		var fixture = new MultiplayerTestSupport.Fixture();
		var dispatcher = MakeDispatcher(fixture: fixture);
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		fixture.RegisterAll(host);
		var existing = fixture.CreateMatch(host);

		var reply = await Run(dispatcher, host, "!mp makeprivate", existing);

		var created = fixture.MatchRegistry.GetByDbId(host.MpScopeMatchId!.Value);
		Assert.NotNull(created);
		Assert.Contains(string.Format(MpReplies.CreatedMatch, created.DbId, created.Name, " (private)"), reply);
		Assert.NotEqual(existing.DbId, created.DbId);
		Assert.True(created.IsPrivate);
		Assert.False(existing.IsPrivate);
		// Already seated in `existing`, so creating another room doesn't move the physical seat.
		Assert.Same(existing, host.Match);
	}

	[Fact]
	public async Task DispatchAsync_MpJoin_NonExistent_ReturnsError()
	{
		var dispatcher = MakeDispatcher();
		var sender = MultiplayerTestSupport.MakePlayer(1, "userSession");

		var reply = await Run(dispatcher, sender, "!mp join 999", null);

		Assert.Contains(string.Format(MpReplies.NoActiveMatchWithId, 999), reply);
	}

	[Fact]
	public async Task DispatchAsync_MpJoin_Private_Fails()
	{
		var fixture = new MultiplayerTestSupport.Fixture();
		var dispatcher = MakeDispatcher(fixture: fixture);
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var player = MultiplayerTestSupport.MakePlayer(2, "userSession");
		fixture.RegisterAll(host, player);
		var match = fixture.CreateMatch(host);
		match.IsPrivate = true;

		var reply = await Run(dispatcher, player, $"!mp join {match.DbId}", null);

		Assert.Contains(string.Format(MpReplies.PrivateRoomJoinDenied, match.DbId), reply);
	}

	[Fact]
	public async Task DispatchAsync_MpJoin_Normal_Succeeds()
	{
		var fixture = new MultiplayerTestSupport.Fixture();
		var dispatcher = MakeDispatcher(fixture: fixture);
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var player = MultiplayerTestSupport.MakePlayer(2, "userSession");
		fixture.RegisterAll(host, player);
		var match = fixture.CreateMatch(host);

		var reply = await Run(dispatcher, player, $"!mp join {match.DbId}", null);

		Assert.NotNull(reply);
		Assert.Contains(string.Format(MpReplies.JoinedMatch, match.DbId, match.Name), reply);
	}

	[Fact]
	public async Task DispatchAsync_MpPrivate_Referee_ShowsStatus()
	{
		var fixture = new MultiplayerTestSupport.Fixture();
		var dispatcher = MakeDispatcher(fixture: fixture);
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		fixture.RegisterAll(host);
		var match = fixture.CreateMatch(host);

		var reply = await Run(dispatcher, host, "!mp private", match);

		Assert.Contains(string.Format(MpReplies.MatchIsPrivateNow, "not private"), reply);
	}

	[Fact]
	public async Task DispatchAsync_CustomPrefix_IsRespected()
	{
		var dispatcher = MakeDispatcher(".");
		var sender = MultiplayerTestSupport.MakePlayer(1, "cmyui");

		Assert.Null(await Run(dispatcher, sender, "!roll", null));
		Assert.NotNull(await Run(dispatcher, sender, ".roll", null));
	}

	[Fact]
	public async Task DispatchAsync_Where_KnownUser_ReturnsCountry()
	{
		var dispatcher = MakeDispatcher();
		var sender = MultiplayerTestSupport.MakePlayer(1, "cmyui");
		_users.FetchByNameAsync("peppy", Arg.Any<CancellationToken>()).Returns(MakeUser("peppy", "us"));

		var reply = await Run(dispatcher, sender, "!where peppy", null);

		Assert.Equal(string.Format(MpReplies.WhereIsIn, "peppy", "United States"), reply);
	}

	[Fact]
	public async Task DispatchAsync_Where_UnknownUser_ReturnsNotRegistered()
	{
		var dispatcher = MakeDispatcher();
		var sender = MultiplayerTestSupport.MakePlayer(1, "cmyui");
		_users.FetchByNameAsync("ghost", Arg.Any<CancellationToken>()).Returns((User?)null);

		var reply = await Run(dispatcher, sender, "!where ghost", null);

		Assert.Equal(string.Format(MpReplies.NotRegistered, "ghost"), reply);
	}

	[Fact]
	public async Task DispatchAsync_Where_PseudoCountryCode_FallsBackToRawCode()
	{
		var dispatcher = MakeDispatcher();
		var sender = MultiplayerTestSupport.MakePlayer(1, "cmyui");
		_users.FetchByNameAsync("nobody", Arg.Any<CancellationToken>()).Returns(MakeUser("nobody", "xx"));

		var reply = await Run(dispatcher, sender, "!where nobody", null);

		Assert.Equal(string.Format(MpReplies.WhereIsIn, "nobody", "Unknown"), reply);
	}

	[Fact]
	public async Task DispatchAsync_Faq_KnownEntry_ReturnsFileContentsOneLinePerLine()
	{
		var faqsPath = CreateTempFaqsDir();
		try
		{
			await File.WriteAllLinesAsync(Path.Combine(faqsPath, "rules.txt"), ["Line one", "Line two"]);
			var dispatcher = MakeDispatcher(storageOptions: MakeStorageOptions(faqsPath));
			var sender = MultiplayerTestSupport.MakePlayer(1, "cmyui");

			var reply = await Run(dispatcher, sender, "!faq rules", null);

			Assert.Equal("Line one\nLine two", reply);
		}
		finally
		{
			Directory.Delete(faqsPath, true);
		}
	}

	[Fact]
	public async Task DispatchAsync_Faq_UnknownEntry_ReturnsNotFound()
	{
		var faqsPath = CreateTempFaqsDir();
		try
		{
			var dispatcher = MakeDispatcher(storageOptions: MakeStorageOptions(faqsPath));
			var sender = MultiplayerTestSupport.MakePlayer(1, "cmyui");

			var reply = await Run(dispatcher, sender, "!faq nonexistent", null);

			Assert.Equal(string.Format(MpReplies.NoFaqEntryFound, "nonexistent"), reply);
		}
		finally
		{
			Directory.Delete(faqsPath, true);
		}
	}

	[Fact]
	public async Task DispatchAsync_FaqList_ListsEntriesSortedAndIgnoresListTxt()
	{
		var faqsPath = CreateTempFaqsDir();
		try
		{
			await File.WriteAllTextAsync(Path.Combine(faqsPath, "rules.txt"), "rules");
			await File.WriteAllTextAsync(Path.Combine(faqsPath, "peppy.txt"), "peppy");
			await File.WriteAllTextAsync(Path.Combine(faqsPath, "list.txt"), "should be ignored");
			var dispatcher = MakeDispatcher(storageOptions: MakeStorageOptions(faqsPath));
			var sender = MultiplayerTestSupport.MakePlayer(1, "cmyui");

			var reply = await Run(dispatcher, sender, "!faq list", null);

			Assert.Equal(string.Format(MpReplies.AvailableFaqEntries, "peppy, rules"), reply);
		}
		finally
		{
			Directory.Delete(faqsPath, true);
		}
	}

	[Fact]
	public async Task DispatchAsync_FaqList_NoEntries_ReturnsNoneAvailable()
	{
		var faqsPath = CreateTempFaqsDir();
		try
		{
			var dispatcher = MakeDispatcher(storageOptions: MakeStorageOptions(faqsPath));
			var sender = MultiplayerTestSupport.MakePlayer(1, "cmyui");

			var reply = await Run(dispatcher, sender, "!faq list", null);

			Assert.Equal(MpReplies.NoFaqEntriesAvailable, reply);
		}
		finally
		{
			Directory.Delete(faqsPath, true);
		}
	}

	[Fact]
	public async Task DispatchAsync_Faq_PathTraversalAttempt_NeverEscapesFaqsDir()
	{
		var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
		var faqsPath = Path.Combine(root, "faqs");
		Directory.CreateDirectory(faqsPath);
		try
		{
			// A canary file OUTSIDE FaqsPath — if traversal ever worked, this is what would leak.
			await File.WriteAllTextAsync(Path.Combine(root, "secret.txt"), "TOP SECRET");
			var dispatcher = MakeDispatcher(storageOptions: MakeStorageOptions(faqsPath));
			var sender = MultiplayerTestSupport.MakePlayer(1, "cmyui");

			var reply = await Run(dispatcher, sender, "!faq ../secret", null);

			Assert.DoesNotContain("TOP SECRET", reply);
			Assert.Equal(string.Format(MpReplies.NoFaqEntryFound, "secret"), reply);
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public async Task DispatchAsync_Faq_EntryWithSpaces_ReturnsFileContents()
	{
		var faqsPath = CreateTempFaqsDir();
		try
		{
			await File.WriteAllTextAsync(Path.Combine(faqsPath, "mod requests.txt"), "no mod requests here");
			var dispatcher = MakeDispatcher(storageOptions: MakeStorageOptions(faqsPath));
			var sender = MultiplayerTestSupport.MakePlayer(1, "cmyui");

			var reply = await Run(dispatcher, sender, "!faq mod requests", null);

			Assert.Equal("no mod requests here", reply);
		}
		finally
		{
			Directory.Delete(faqsPath, true);
		}
	}

	[Fact]
	public async Task DispatchAsync_Faq_BackslashTraversalAttempt_Rejected()
	{
		var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
		var faqsPath = Path.Combine(root, "faqs");
		Directory.CreateDirectory(faqsPath);
		try
		{
			await File.WriteAllTextAsync(Path.Combine(root, "secret.txt"), "TOP SECRET");
			var dispatcher = MakeDispatcher(storageOptions: MakeStorageOptions(faqsPath));
			var sender = MultiplayerTestSupport.MakePlayer(1, "cmyui");

			var reply = await Run(dispatcher, sender, "!faq ..\\secret", null);

			Assert.DoesNotContain("TOP SECRET", reply);
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public async Task DispatchAsync_MpIn_RefereeOfOtherMatch_ScopesAndRoutesFutureCommands()
	{
		var fixture = new MultiplayerTestSupport.Fixture();
		var dispatcher = MakeDispatcher(fixture: fixture);
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var referee = MultiplayerTestSupport.MakePlayer(2, "ref");
		fixture.RegisterAll(host, referee);
		var match = fixture.CreateMatch(host);
		match.AddReferee(referee.Id);

		var inReply = await Run(dispatcher, referee, $"!mp in {match.DbId}", null);
		Assert.Contains($"#{match.DbId}", inReply);

		var settingsReply = await Run(dispatcher, referee, "!mp settings", null);
		Assert.Contains($"#{match.DbId}", settingsReply);
	}

	[Fact]
	public async Task DispatchAsync_MpIn_NotReferee_Rejected()
	{
		var fixture = new MultiplayerTestSupport.Fixture();
		var dispatcher = MakeDispatcher(fixture: fixture);
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var other = MultiplayerTestSupport.MakePlayer(2, "other");
		fixture.RegisterAll(host, other);
		var match = fixture.CreateMatch(host);

		var reply = await Run(dispatcher, other, $"!mp in {match.DbId}", null);

		Assert.Contains(string.Format(MpReplies.NotARefereeOfMatch, match.DbId), reply);
		Assert.Null(other.MpScopeMatchId);
	}

	[Fact]
	public async Task DispatchAsync_MpIn_FromRealChannel_RepliesDmOnly()
	{
		var fixture = new MultiplayerTestSupport.Fixture();
		var bot = MultiplayerTestSupport.MakePlayer(BotBootstrapService.BotId, "BasilBot");
		fixture.SessionRegistry.GetByUserId(BotBootstrapService.BotId).Returns(bot);
		var dispatcher = MakeDispatcher(fixture: fixture);
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var referee = new IrcSession(2, "ref", "irc-2", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
			{ IrcConnection = new RecordingIrcConnection() };
		fixture.RegisterAll(host);
		fixture.IrcSessionRegistry.GetByUserId(2).Returns(referee);
		var match = fixture.CreateMatch(host);
		match.AddReferee(referee.Id);

		var sink = new RecordingReplySink();
		await dispatcher.DispatchAsync(referee, $"!mp in {match.DbId}", null, "#osu", sink);

		Assert.Null(referee.MpScopeMatchId);
		// The refusal is DM'd back rather than posted into #osu.
		Assert.Empty(sink.Replies);
		Assert.Empty(sink.DmReplies);
		var connection = (RecordingIrcConnection)referee.IrcConnection;
		Assert.Contains(connection.Received,
			m => m.Command == "PRIVMSG" && m.Params[1] == MpReplies.MpInDmOnly);
	}

	[Fact]
	public async Task DispatchAsync_ScopedSubcommand_FromUnrelatedChannel_NoPhysicalSeatFallback_NotScoped()
	{
		// The "physically seated" fallback only applies to a DM (channelName is null) — from any real
		// channel like #osu, a sender with no !mp in scope and no channel-derived scope gets the
		// "not scoped" reply instead of silently resolving to whatever match they happen to be seated in.
		var fixture = new MultiplayerTestSupport.Fixture();
		var bot = MultiplayerTestSupport.MakePlayer(BotBootstrapService.BotId, "BasilBot");
		fixture.SessionRegistry.GetByUserId(BotBootstrapService.BotId).Returns(bot);
		var dispatcher = MakeDispatcher(fixture: fixture);
		var hostIrc = new IrcSession(1, "host", "irc-1", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
			{ IrcConnection = new RecordingIrcConnection() };
		fixture.IrcSessionRegistry.GetByUserId(1).Returns(hostIrc);
		fixture.CreateMatch(hostIrc); // scoped to nothing — !mp in was never called

		var sink = new RecordingReplySink();
		await dispatcher.DispatchAsync(hostIrc, "!mp settings", null, "#osu", sink);

		Assert.Empty(sink.Replies);
		Assert.Empty(sink.DmReplies);
		var connection = (RecordingIrcConnection)hostIrc.IrcConnection;
		Assert.Contains(connection.Received,
			m => m.Command == "PRIVMSG" && m.Params[1].Contains(MpReplies.NotScopedToAnyMatchHint));
	}

	[Fact]
	public async Task DispatchAsync_ScopedSubcommand_FromUnrelatedChannel_WithMpInScope_RepliesViaDmOnly()
	{
		var fixture = new MultiplayerTestSupport.Fixture();
		var bot = MultiplayerTestSupport.MakePlayer(BotBootstrapService.BotId, "BasilBot");
		fixture.SessionRegistry.GetByUserId(BotBootstrapService.BotId).Returns(bot);
		var dispatcher = MakeDispatcher(fixture: fixture);
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var refereeIrc = new IrcSession(2, "ircref", "irc-2", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
			{ IrcConnection = new RecordingIrcConnection() };
		fixture.RegisterAll(host);
		fixture.IrcSessionRegistry.GetByUserId(2).Returns(refereeIrc);
		var match = fixture.CreateMatch(host);
		match.AddReferee(refereeIrc.Id);

		// !mp in via a genuine DM (channelName null) establishes the scope.
		var dmSink = new RecordingReplySink();
		await dispatcher.DispatchAsync(refereeIrc, $"!mp in {match.DbId}", null, null, dmSink);
		Assert.Equal(match.DbId, refereeIrc.MpScopeMatchId);

		// A scoped subcommand typed in an unrelated channel (#osu) never posts into it.
		var osuSink = new RecordingReplySink();
		await dispatcher.DispatchAsync(refereeIrc, "!mp settings", null, "#osu", osuSink);

		Assert.Empty(osuSink.Replies);
		Assert.Empty(osuSink.DmReplies);

		var connection = (RecordingIrcConnection)refereeIrc.IrcConnection;
		var dmsToSelf = connection.Received
			.Where(m => m.Command == "PRIVMSG" && m.Params[0] == refereeIrc.Name)
			.ToList();
		Assert.NotEmpty(dmsToSelf);
		Assert.All(dmsToSelf, m => Assert.StartsWith($"[#{match.DbId}]", m.Params[1]));
	}

	[Fact]
	public async Task DispatchAsync_ScopedSubcommand_FromMatchsOwnChannel_RepliesPubliclyAsBefore()
	{
		var fixture = new MultiplayerTestSupport.Fixture();
		var bot = MultiplayerTestSupport.MakePlayer(BotBootstrapService.BotId, "BasilBot");
		fixture.SessionRegistry.GetByUserId(BotBootstrapService.BotId).Returns(bot);
		var dispatcher = MakeDispatcher(fixture: fixture);
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		fixture.RegisterAll(host);
		var match = fixture.CreateMatch(host);

		var sink = new RecordingReplySink();
		await dispatcher.DispatchAsync(host, "!mp settings", match, match.ChatChannelName, sink);

		Assert.NotEmpty(sink.Replies);
	}

	[Fact]
	public async Task DispatchAsync_MpSubcommand_FromLobby_RepliesDmError()
	{
		var fixture = new MultiplayerTestSupport.Fixture();
		var bot = MultiplayerTestSupport.MakePlayer(BotBootstrapService.BotId, "BasilBot");
		fixture.SessionRegistry.GetByUserId(BotBootstrapService.BotId).Returns(bot);
		var dispatcher = MakeDispatcher(fixture: fixture);
		var sender = new IrcSession(1, "host", "irc-1", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
			{ IrcConnection = new RecordingIrcConnection() };

		var sink = new RecordingReplySink();
		await dispatcher.DispatchAsync(sender, "!mp settings", null, "#lobby", sink);

		// #lobby is a shared channel — the refusal is DM'd back, never posted there.
		Assert.Empty(sink.Replies);
		Assert.Empty(sink.DmReplies);
		var connection = (RecordingIrcConnection)sender.IrcConnection;
		Assert.Contains(connection.Received,
			m => m.Command == "PRIVMSG" && m.Params[1] == string.Format(MpReplies.MpNotUsableFromLobby, "settings"));
	}

	[Fact]
	public async Task DispatchAsync_Chain_FromLobby_RepliesDmError()
	{
		var fixture = new MultiplayerTestSupport.Fixture();
		var bot = MultiplayerTestSupport.MakePlayer(BotBootstrapService.BotId, "BasilBot");
		fixture.SessionRegistry.GetByUserId(BotBootstrapService.BotId).Returns(bot);
		var dispatcher = MakeDispatcher(fixture: fixture);
		var sender = new IrcSession(1, "host", "irc-1", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
			{ IrcConnection = new RecordingIrcConnection() };

		var sink = new RecordingReplySink();
		await dispatcher.DispatchAsync(sender, "!mp lock; !mp size 4", null, "#lobby", sink);

		Assert.Empty(sink.Replies);
		Assert.Empty(sink.DmReplies);
		var connection = (RecordingIrcConnection)sender.IrcConnection;
		Assert.Contains(connection.Received,
			m => m.Command == "PRIVMSG" && m.Params[1] == MpReplies.MpChainNotUsableFromLobby);
	}

	[Fact]
	public async Task DispatchAsync_MpNonReferee_FromMatchsOwnChannel_RepliesPublicError()
	{
		var fixture = new MultiplayerTestSupport.Fixture();
		var dispatcher = MakeDispatcher(fixture: fixture);
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var other = MultiplayerTestSupport.MakePlayer(2, "other");
		fixture.RegisterAll(host, other);
		var match = fixture.CreateMatch(host);

		// The match's own channel is not a shared one — the error is public there, not DM'd.
		var reply = await Run(dispatcher, other, "!mp lock", match);

		Assert.Equal(string.Format(MpReplies.NotARefereeOfMatch, match.DbId), reply);
	}

	[Fact]
	public async Task DispatchAsync_Chain_NonReferee_RepliesError()
	{
		var fixture = new MultiplayerTestSupport.Fixture();
		var dispatcher = MakeDispatcher(fixture: fixture);
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var other = MultiplayerTestSupport.MakePlayer(2, "other");
		fixture.RegisterAll(host, other);
		var match = fixture.CreateMatch(host);

		var reply = await Run(dispatcher, other, "!mp lock; !mp size 4", match);

		Assert.Equal(string.Format(MpReplies.NotARefereeOfMatch, match.DbId), reply);
	}

	[Fact]
	public async Task DispatchAsync_MpUnknownSubcommand_RepliesError()
	{
		var fixture = new MultiplayerTestSupport.Fixture();
		var dispatcher = MakeDispatcher(fixture: fixture);
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		fixture.RegisterAll(host);
		var match = fixture.CreateMatch(host);

		var reply = await Run(dispatcher, host, "!mp bogus", match);

		Assert.Equal(string.Format(MpReplies.UnknownMpSubcommand, "bogus"), reply);
	}

	[Fact]
	public async Task DispatchAsync_ScopeOverridesLiteralChannel()
	{
		var fixture = new MultiplayerTestSupport.Fixture();
		var dispatcher = MakeDispatcher(fixture: fixture);
		var hostA = MultiplayerTestSupport.MakePlayer(1, "hostA");
		var hostB = MultiplayerTestSupport.MakePlayer(2, "hostB");
		fixture.RegisterAll(hostA, hostB);
		var matchA = fixture.CreateMatch(hostA);
		var matchB = fixture.CreateMatch(hostB);
		matchA.AddReferee(hostB.Id);
		hostB.MpScopeMatchId = matchA.DbId;

		// hostB is physically sitting in matchB's own channel, but stays scoped to matchA.
		var reply = await Run(dispatcher, hostB, "!mp settings", matchB);

		Assert.Contains($"#{matchA.DbId}", reply);
	}

	[Fact]
	public async Task DispatchAsync_Chain_SemicolonRunsBothSegmentsRegardlessOfOutcome()
	{
		var fixture = new MultiplayerTestSupport.Fixture();
		var dispatcher = MakeDispatcher(fixture: fixture);
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		fixture.RegisterAll(host);
		var match = fixture.CreateMatch(host);

		var replies = await RunAll(dispatcher, host, "!mp host; !mp name Renamed", match);

		Assert.Contains(replies, r => r.Contains(MpReplies.HostUsage));
		Assert.Contains(replies, r => r.Contains(string.Format(MpReplies.RoomNameUpdated, "Renamed"),
			StringComparison.OrdinalIgnoreCase));
		Assert.Equal("Renamed", match.Name);
	}

	[Fact]
	public async Task DispatchAsync_Chain_AndShortCircuitsAfterFailure()
	{
		var fixture = new MultiplayerTestSupport.Fixture();
		var dispatcher = MakeDispatcher(fixture: fixture);
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		fixture.RegisterAll(host);
		var match = fixture.CreateMatch(host);

		var reply = await Run(dispatcher, host, "!mp host && !mp name Renamed", match);

		Assert.Contains(MpReplies.HostUsage, reply);
		Assert.DoesNotContain(string.Format(MpReplies.RoomNameUpdated, "Renamed"), reply,
			StringComparison.OrdinalIgnoreCase);
		Assert.NotEqual("Renamed", match.Name);
	}

	[Fact]
	public async Task DispatchAsync_Chain_AndRunsSecondAfterFirstSucceeds()
	{
		var fixture = new MultiplayerTestSupport.Fixture();
		var dispatcher = MakeDispatcher(fixture: fixture);
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		fixture.RegisterAll(host);
		var match = fixture.CreateMatch(host);

		await Run(dispatcher, host, "!mp name First && !mp name Second", match);

		Assert.Equal("Second", match.Name);
	}

	[Fact]
	public async Task DispatchAsync_Chain_QuotedSemicolonIsNotTreatedAsDelimiter()
	{
		var fixture = new MultiplayerTestSupport.Fixture();
		var dispatcher = MakeDispatcher(fixture: fixture);
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		fixture.RegisterAll(host);
		var match = fixture.CreateMatch(host);

		await Run(dispatcher, host, "!mp name \"a;b\"", match);

		Assert.Equal("a;b", match.Name);
	}

	[Fact]
	public async Task DispatchAsync_Chain_EscapedQuoteAndBackslashAreResolved()
	{
		var fixture = new MultiplayerTestSupport.Fixture();
		var dispatcher = MakeDispatcher(fixture: fixture);
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		fixture.RegisterAll(host);
		var match = fixture.CreateMatch(host);

		await Run(dispatcher, host, "!mp name \"a \\\" b \\\\ c\"", match);

		Assert.Equal("a \" b \\ c", match.Name);
	}

	[Fact]
	public async Task DispatchAsync_Chain_RejectsNonLocalSegment()
	{
		var fixture = new MultiplayerTestSupport.Fixture();
		var dispatcher = MakeDispatcher(fixture: fixture);
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		fixture.RegisterAll(host);
		var match = fixture.CreateMatch(host);

		var reply = await Run(dispatcher, host, "!mp name Foo; !roll 100", match);

		Assert.Contains(string.Format(MpReplies.ChainMustBeMp, "!", "!roll 100"), reply);
		Assert.NotEqual("Foo", match.Name);
	}

	private static string CreateTempFaqsDir()
	{
		var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
		Directory.CreateDirectory(path);
		return path;
	}

	private static User MakeUser(string name, string country)
	{
		return new User(1, name, Enum.Parse<Country>(country, true), UserPrivileges.Unrestricted, default);
	}

	/// <summary>Captures what a command sent through <see cref="ICommandReplySink" /> instead of returning it.</summary>
	private sealed class RecordingReplySink : ICommandReplySink
	{
		public List<string> Replies { get; } = [];
		public List<string> DmReplies { get; } = [];

		public string? Last => Replies.Count > 0 ? Replies[^1] : DmReplies.Count > 0 ? DmReplies[^1] : null;

		public void Reply(string text)
		{
			Replies.Add(text);
		}

		public void ReplyDm(string text)
		{
			DmReplies.Add(text);
		}
	}

	/// <summary>Captures what would be sent over a real IRC connection, for asserting DM-redirect behavior.</summary>
	private sealed class RecordingIrcConnection : IIrcConnection
	{
		public List<IrcMessage> Received { get; } = [];
		public UserSession User => throw new NotImplementedException();

		public void Send(IrcMessage message)
		{
			Received.Add(message);
		}
	}
}