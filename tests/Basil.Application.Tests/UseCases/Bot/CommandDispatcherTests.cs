using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Bot;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configurations;
using Basil.Application.Services.Bot;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Multiplayer;
using Basil.Application.Tests.PacketHandlers;
using Basil.Domain.Login;
using Basil.Domain.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Basil.Application.Tests.UseCases.Bot;

public class CommandDispatcherTests
{
	private readonly IBeatmapRepository _beatmaps = Substitute.For<IBeatmapRepository>();
	private readonly IUserRepository _users = Substitute.For<IUserRepository>();

	/// <summary>
	///     Mirrors the old `dispatcher.DispatchAsync(sender, message, matchScope, prefixOptional)` shape
	///     so call sites don't need per-test channel plumbing — a non-null <paramref name="matchScope" />
	///     is inferred to mean "sent in that match's own chat channel", matching every existing test's
	///     actual intent (see <see cref="ICommandDispatcher.DispatchAsync" />'s doc comment on matchScope).
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
	///     needed for a `;`-chained line, where each segment now sends its own reply immediately instead
	///     of the dispatcher joining them into one string.
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
			ReplaysPath = "", AvatarsPath = "", MapsetsPath = "", SeasonalsPath = "", FaqsPath = faqsPath,
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
			fixture.SessionRegistry, Substitute.For<IUserRepository>(), NullLogger<MpCommandService>.Instance,
			NullLogger<MatchControlService>.Instance);
		return new CommandDispatcher(options, mpCommands, _users,
			Options.Create(storageOptions ?? MakeStorageOptions()),
			fixture.MatchRegistry, NullLogger<CommandDispatcher>.Instance);
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
		Assert.StartsWith("cmyui rolls ", reply);
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
		Assert.StartsWith("cmyui rolls ", reply);
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

		Assert.Contains("not scoped to a match", reply);
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
		Assert.Contains("Created the match", reply);
		Assert.NotNull(sender.Match);
	}

	[Fact]
	public async Task DispatchAsync_MpMakeprivateWithoutMatchScope_RepliesNotScoped()
	{
		var fixture = new MultiplayerTestSupport.Fixture();
		var dispatcher = MakeDispatcher(fixture: fixture);
		var sender = MultiplayerTestSupport.MakePlayer(1, "creator");
		fixture.RegisterAll(sender);

		var reply = await Run(dispatcher, sender, "!mp makeprivate Room", null);

		Assert.Contains("not scoped to a match", reply);
	}

	[Fact]
	public async Task DispatchAsync_MpMakeprivateWithMatchScope_SetsPrivate()
	{
		var fixture = new MultiplayerTestSupport.Fixture();
		var dispatcher = MakeDispatcher(fixture: fixture);
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		fixture.RegisterAll(host);
		var match = fixture.CreateMatch(host);

		var reply = await Run(dispatcher, host, "!mp makeprivate", match);

		Assert.True(match.IsPrivate);
		Assert.Contains("now private", reply);
	}

	[Fact]
	public async Task DispatchAsync_MpJoin_NonExistent_ReturnsError()
	{
		var dispatcher = MakeDispatcher();
		var sender = MultiplayerTestSupport.MakePlayer(1, "userSession");

		var reply = await Run(dispatcher, sender, "!mp join 999", null);

		Assert.Contains("No active match", reply);
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

		Assert.Contains("private", reply);
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
		Assert.Contains("Joined match", reply);
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

		Assert.Contains("not private", reply);
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

		Assert.Equal("peppy is in United States", reply);
	}

	[Fact]
	public async Task DispatchAsync_Where_UnknownUser_ReturnsNotRegistered()
	{
		var dispatcher = MakeDispatcher();
		var sender = MultiplayerTestSupport.MakePlayer(1, "cmyui");
		_users.FetchByNameAsync("ghost", Arg.Any<CancellationToken>()).Returns((User?)null);

		var reply = await Run(dispatcher, sender, "!where ghost", null);

		Assert.Equal("ghost is not registered.", reply);
	}

	[Fact]
	public async Task DispatchAsync_Where_PseudoCountryCode_FallsBackToRawCode()
	{
		var dispatcher = MakeDispatcher();
		var sender = MultiplayerTestSupport.MakePlayer(1, "cmyui");
		_users.FetchByNameAsync("nobody", Arg.Any<CancellationToken>()).Returns(MakeUser("nobody", "xx"));

		var reply = await Run(dispatcher, sender, "!where nobody", null);

		Assert.Equal("nobody is in Unknown", reply);
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

			Assert.Equal("No FAQ entry found for 'nonexistent'.", reply);
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

			Assert.Equal("Available FAQ entries: peppy, rules", reply);
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

			Assert.Equal("No FAQ entries available.", reply);
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
			Assert.Equal("No FAQ entry found for 'secret'.", reply);
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

		Assert.Contains("not a referee", reply);
		Assert.Null(other.MpScopeMatchId);
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

		Assert.Contains(replies, r => r.Contains("Usage: !mp host"));
		Assert.Contains(replies, r => r.Contains("renamed", StringComparison.OrdinalIgnoreCase));
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

		Assert.Contains("Usage: !mp host", reply);
		Assert.DoesNotContain("renamed", reply, StringComparison.OrdinalIgnoreCase);
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

		Assert.Contains("rejected at", reply);
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
}