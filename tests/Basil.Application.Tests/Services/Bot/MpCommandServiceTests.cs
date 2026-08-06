using System.Text;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Bot;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configurations;
using Basil.Application.Services.Bot;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Irc;
using Basil.Application.Sessions.Multiplayer;
using Basil.Application.Tests.Packets;
using Basil.Domain.Beatmaps;
using Basil.Domain.Login;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Protocol.Irc;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Basil.Application.Tests.Services.Bot;

/// <summary>
///     Every subcommand except "help" requires MatchSession.IsReferee — that gate, plus a
///     representative sample of the wrapper-over-existing-MatchMembershipService commands, is what's
///     covered here. Not every argument-validation branch is exercised (they're all short, obvious
///     `return "Usage: ..."` guards).
/// </summary>
public class MpCommandServiceTests
{
	private readonly IBeatmapRepository _beatmaps = Substitute.For<IBeatmapRepository>();
	private readonly MultiplayerTestSupport.Fixture _fixture = new();
	private readonly IUserRepository _users = Substitute.For<IUserRepository>();

	private MpCommandService MakeService()
	{
		return new MpCommandService(_fixture.MatchMembership, _fixture.MatchRegistry, _fixture.MatchRepository,
			_beatmaps,
			_fixture.SessionRegistry, _users, _fixture.ChannelRegistry, NullLogger<MpCommandService>.Instance,
			NullLogger<MatchControlService>.Instance);
	}

	private static async Task<string?> Run(MpCommandService svc, UserSession sender, MatchSession match,
		string subcommand, IReadOnlyList<string> args)
	{
		var sink = new RecordingReplySink();
		await svc.TryHandleAsync(sender, match, subcommand, args, sink);
		return sink.Last;
	}

	private static async Task<string?> RunMake(MpCommandService svc, UserSession sender, IReadOnlyList<string> args,
		bool isPrivate = false)
	{
		var sink = new RecordingReplySink();
		await svc.MakeAsync(sender, args, sink, isPrivate);
		return sink.Last;
	}

	private static async Task<string?> RunJoin(MpCommandService svc, UserSession sender, IReadOnlyList<string> args)
	{
		var sink = new RecordingReplySink();
		await svc.JoinAsync(sender, args, sink);
		return sink.Last;
	}

	private static string? RunSetScope(MpCommandService svc, UserSession sender, IReadOnlyList<string> args)
	{
		var sink = new RecordingReplySink();
		svc.SetScopeAsync(sender, args, sink);
		return sink.Last;
	}

	[Fact]
	public async Task HandleAsync_NonReferee_SilentlyIgnored()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var other = MultiplayerTestSupport.MakePlayer(2, "other");
		_fixture.RegisterAll(host, other);
		var match = _fixture.CreateMatch(host);

		var reply = await Run(MakeService(), other, match, "lock", ["1"]);

		Assert.Null(reply);
	}

	[Fact]
	public async Task HandleAsync_Help_AnyMatchMember_ReturnsHelpText()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var other = MultiplayerTestSupport.MakePlayer(2, "other");
		_fixture.RegisterAll(host, other);
		var match = _fixture.CreateMatch(host);

		var reply = await Run(MakeService(), other, match, "help", []);

		Assert.NotNull(reply);
		Assert.Contains("settings", reply);
	}

	[Fact]
	public async Task HandleAsync_Lock_SetsRoomLockedAndBlocksNewJoins()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var other = MultiplayerTestSupport.MakePlayer(2, "other");
		_fixture.RegisterAll(host, other);
		var match = _fixture.CreateMatch(host);

		var reply = await Run(MakeService(), host, match, "lock", []);

		Assert.True(match.IsLocked);
		Assert.Equal("Locked the match", reply);
		Assert.NotEqual(MatchMembershipService.JoinResult.Ok, await _fixture.MatchMembership.JoinAsync(other, match, ""));
		Assert.Null(other.Match);
	}

	[Fact]
	public async Task HandleAsync_Unlock_ClearsRoomLockedAndAllowsJoins()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var other = MultiplayerTestSupport.MakePlayer(2, "other");
		_fixture.RegisterAll(host, other);
		var match = _fixture.CreateMatch(host);
		match.IsLocked = true;

		var reply = await Run(MakeService(), host, match, "unlock", []);

		Assert.False(match.IsLocked);
		Assert.Equal("Unlocked the match", reply);
		Assert.Equal(MatchMembershipService.JoinResult.Ok, await _fixture.MatchMembership.JoinAsync(other, match, ""));
	}

	[Fact]
	public async Task HandleAsync_Size_LocksSlotsBeyondLimit()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);

		await Run(MakeService(), host, match, "size", ["4"]);

		Assert.Equal(SlotStatus.Open, match.Slots[3].Status);
		Assert.Equal(SlotStatus.Locked, match.Slots[4].Status);
		Assert.Equal(SlotStatus.Locked, match.Slots[15].Status);
	}

	[Theory]
	[InlineData("32", 16)]
	[InlineData("0", 1)]
	[InlineData("-5", 1)]
	public async Task HandleAsync_Size_OutOfRange_Clamps(string arg, int expectedSize)
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);

		var reply = await Run(MakeService(), host, match, "size", [arg]);

		Assert.Equal($"Changed match to size {expectedSize}", reply);
	}

	[Fact]
	public async Task HandleAsync_Move_RelocatesTargetToOpenSlot()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var other = MultiplayerTestSupport.MakePlayer(2, "other");
		_fixture.RegisterAll(host, other);
		var match = _fixture.CreateMatch(host);
		await _fixture.MatchMembership.JoinAsync(other, match, "");

		var reply = await Run(MakeService(), host, match, "move", ["other", "5"]);

		Assert.Equal(2, match.Slots[4].PlayerId);
		Assert.True(match.Slots[1].Empty);
		Assert.Equal("Moved other into slot 5", reply);
	}

	[Fact]
	public async Task HandleAsync_Move_OutOfRangeSlot_Clamps()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var other = MultiplayerTestSupport.MakePlayer(2, "other");
		_fixture.RegisterAll(host, other);
		var match = _fixture.CreateMatch(host);
		await _fixture.MatchMembership.JoinAsync(other, match, "");

		var reply = await Run(MakeService(), host, match, "move", ["other", "99"]);

		Assert.Equal(2, match.Slots[15].PlayerId);
		Assert.Equal("Moved other into slot 16", reply);
	}

	[Fact]
	public async Task HandleAsync_Host_TransfersHostToTargetInMatch()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var other = MultiplayerTestSupport.MakePlayer(2, "other");
		_fixture.RegisterAll(host, other);
		var match = _fixture.CreateMatch(host);
		await _fixture.MatchMembership.JoinAsync(other, match, "");

		var reply = await Run(MakeService(), host, match, "host", ["other"]);

		Assert.Equal(2, match.HostId);
		Assert.Equal("Changed match host to other", reply);
	}

	[Fact]
	public async Task HandleAsync_ClearHost_SetsHostIdToZero()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);

		await Run(MakeService(), host, match, "clearhost", []);

		Assert.Equal(0, match.HostId);
	}

	[Fact]
	public async Task HandleAsync_Name_RenamesMatch()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);

		await Run(MakeService(), host, match, "name", ["New", "Name"]);

		Assert.Equal("New Name", match.Name);
	}

	[Fact]
	public async Task HandleAsync_PasswordNoArgs_ClearsPassword()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);
		match.Password = "secret";

		await Run(MakeService(), host, match, "password", []);

		Assert.Equal("", match.Password);
	}

	[Fact]
	public async Task HandleAsync_PasswordOff_IsTreatedAsLiteralText()
	{
		// "off"/"none" are no longer special keywords — only omitting the arg clears the password.
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);

		await Run(MakeService(), host, match, "password", ["off"]);

		Assert.Equal("off", match.Password);
	}

	[Fact]
	public async Task HandleAsync_AddRef_ByExistingReferee_Succeeds()
	{
		// Any referee can add another — referees can act on/manage each other, not just the host.
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var referee = MultiplayerTestSupport.MakePlayer(2, "referee");
		var other = MultiplayerTestSupport.MakePlayer(3, "other");
		_fixture.RegisterAll(host, referee, other);
		var match = _fixture.CreateMatch(host, hostIsReferee: false);
		match.AddReferee(referee.Id);

		var reply = await Run(MakeService(), referee, match, "addref", ["other"]);

		Assert.Contains(other.Id, match.Referees);
		Assert.Equal("Added other to the match referees", reply);
	}

	[Fact]
	public async Task HandleAsync_HostNotAddedAsReferee_ReadOnlyCommandsStillWork()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host, hostIsReferee: false);

		var reply = await Run(MakeService(), host, match, "settings", []);

		// "settings" is a read-only subcommand, open to anyone the match resolves for — not gated on
		// referee status (see MpCommandService.ReadOnlySubcommands).
		Assert.NotNull(reply);
		Assert.DoesNotContain(host.Id, match.Referees);
	}

	[Fact]
	public async Task HandleAsync_NonReadOnlySubcommand_NonReferee_SilentlyIgnored()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host, hostIsReferee: false);

		var reply = await Run(MakeService(), host, match, "lock", []);

		Assert.Null(reply);
		Assert.DoesNotContain(host.Id, match.Referees);
	}

	[Fact]
	public async Task HandleAsync_Settings_BeatmapExists_ShowsBeatmapInfo()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);
		var bmap = MultiplayerTestSupport.MakeBeatmap(match.MapId);
		_beatmaps.FetchOneAsync(match.MapId, cancellationToken: Arg.Any<CancellationToken>()).Returns(bmap);

		var reply = await Run(MakeService(), host, match, "settings", []);

		Assert.Contains($"Beatmap: {bmap.Id} {bmap.FullName}", reply);
	}

	[Fact]
	public async Task HandleAsync_Settings_BeatmapMissingFromDb_ShowsNotFound()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);
		_beatmaps.FetchOneAsync(match.MapId, cancellationToken: Arg.Any<CancellationToken>()).Returns((Beatmap?)null);

		var reply = await Run(MakeService(), host, match, "settings", []);

		Assert.Contains("Beatmap: Not found", reply);
	}

	private static IrcSession MakeIrc(int id, string name)
	{
		return new IrcSession(id, name, $"irc-{id}", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
		{
			Connection = new RecordingIrcConnection()
		};
	}

	/// <summary>Joins a session into a match's own chat channel, using the fixture's shared registries.</summary>
	private void JoinMatchChannel(UserSession session)
	{
		var channelMembership =
			new ChannelMembershipService(_fixture.SessionRegistry, _fixture.ChannelRegistry,
				Options.Create(new IrcOptions()));
		var channel = _fixture.ChannelRegistry.All.Single(c => c.Name.StartsWith("#multi_"));
		channelMembership.Join(session, channel);
	}

	[Fact]
	public async Task HandleAsync_Settings_RefereeAlsoConnectedViaIrc_AppearsInBothRefsAndIrcLists()
	{
		// Three independent questions, not deduplicated: a referee with a separate live IrcSession
		// in the match's own channel shows up in both the Refs and the IRC lists.
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var refereeIrc = MakeIrc(2, "refonirc");
		_fixture.RegisterAll(host);
		_fixture.SessionRegistry.GetSessionsByUserId(2).Returns([refereeIrc]);
		var match = _fixture.CreateMatch(host);
		match.AddReferee(2);
		JoinMatchChannel(refereeIrc);
		_users.FetchByIdAsync(2, Arg.Any<CancellationToken>())
			.Returns(new User(2, "refonirc", Country.Xx, UserPrivileges.Unrestricted, default));

		var reply = await Run(MakeService(), host, match, "settings", []);

		// CreateMatch's default hostIsReferee also makes host a referee, so 2 referees total.
		Assert.Contains("Refs: 2", reply);
		Assert.Contains("#2 refonirc", reply);
		Assert.Contains("IRC: 1", reply);
		var refsIndex = reply!.IndexOf("Refs:", StringComparison.Ordinal);
		var ircIndex = reply.IndexOf("IRC:", StringComparison.Ordinal);
		// "#2 refonirc" must appear once under Refs and once more under IRC — not deduplicated away.
		Assert.Equal(2, reply.Split("#2 refonirc").Length - 1);
		Assert.True(refsIndex < ircIndex);
	}

	[Fact]
	public async Task HandleAsync_Settings_OfflineReferee_ResolvesNameFromUserRepository()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);
		match.AddReferee(99); // never online this session — no GameSession/IrcSession for id 99
		_users.FetchByIdAsync(99, Arg.Any<CancellationToken>())
			.Returns(new User(99, "offlineref", Country.Xx, UserPrivileges.Unrestricted, default));

		var reply = await Run(MakeService(), host, match, "settings", []);

		Assert.Contains("#99 offlineref", reply);
		Assert.DoesNotContain("#99 \n", reply);
	}

	[Fact]
	public async Task HandleAsync_Settings_RefereeUnknownToUserRepository_FallsBackToBareId()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);
		match.AddReferee(999);
		_users.FetchByIdAsync(999, Arg.Any<CancellationToken>()).Returns((User?)null);

		var reply = await Run(MakeService(), host, match, "settings", []);

		Assert.Contains("#999", reply);
		Assert.DoesNotContain("#999 ", reply);
	}

	[Fact]
	public async Task HandleAsync_Settings_SeatedPlayerAlsoOnIrc_AppearsInBothPlayersAndIrcLists()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var hostIrc = MakeIrc(1, "host");
		_fixture.RegisterAll(host);
		_fixture.SessionRegistry.GetSessionsByUserId(1).Returns([host, hostIrc]);
		var match = _fixture.CreateMatch(host);
		JoinMatchChannel(hostIrc);

		var reply = await Run(MakeService(), host, match, "settings", []);

		Assert.Contains("Players: 1", reply);
		Assert.Contains("host", reply); // slot line
		Assert.Contains("IRC: 1", reply);
		Assert.Contains("#1 host", reply);
	}

	[Fact]
	public async Task HandleAsync_Settings_LongUnicodeRefereeList_WrapsWithinIrcWireLimit()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);
		// Vietnamese names (multi-byte UTF-8) — enough of them to force at least one wrap.
		for (var i = 0; i < 20; i++)
		{
			var id = 100 + i;
			match.AddReferee(id);
			_users.FetchByIdAsync(id, Arg.Any<CancellationToken>())
				.Returns(new User(id, $"Người_chơi_số_{i}", Country.Vn, UserPrivileges.Unrestricted, default));
		}

		var reply = await Run(MakeService(), host, match, "settings", []);

		foreach (var line in reply!.Split('\n'))
		{
			var wireLine = IrcMessageWriter.Format(
				IrcMessageWriter.Privmsg("BasilBot", 0, match.ChatChannelName, line));
			Assert.True(Encoding.UTF8.GetByteCount(wireLine) <= 512,
				$"Line exceeds the 512-byte IRC wire limit once framed: {wireLine}");
		}
	}

	private sealed class RecordingIrcConnection : IIrcConnection
	{
		public List<IrcMessage> Received { get; } = [];
		public UserSession User => throw new NotImplementedException();

		public void Send(IrcMessage message)
		{
			Received.Add(message);
		}
	}

	[Fact]
	public async Task HandleAsync_AddRefThenListRefs_ReflectsAddition()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var other = MultiplayerTestSupport.MakePlayer(2, "other");
		_fixture.RegisterAll(host, other);
		var match = _fixture.CreateMatch(host);
		var service = MakeService();

		await Run(service, host, match, "addref", ["other"]);
		var listing = await Run(service, host, match, "listrefs", []);

		Assert.Contains(other.Id, match.Referees);
		Assert.Contains("other", listing);
	}

	[Fact]
	public async Task HandleAsync_RemoveRef_RemovesReferee()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var other = MultiplayerTestSupport.MakePlayer(2, "other");
		_fixture.RegisterAll(host, other);
		var match = _fixture.CreateMatch(host);
		match.AddReferee(other.Id);

		await Run(MakeService(), host, match, "removeref", ["other"]);

		Assert.DoesNotContain(other.Id, match.Referees);
	}

	[Fact]
	public async Task HandleAsync_BanList_ListsBannedNames()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var banned = MultiplayerTestSupport.MakePlayer(2, "banned_guy");
		_fixture.RegisterAll(host, banned);
		var match = _fixture.CreateMatch(host);
		match.AddBan(2);

		var reply = await Run(MakeService(), host, match, "banlist", []);

		Assert.Contains("banned_guy", reply);
	}

	[Fact]
	public async Task HandleAsync_BanList_Empty_ReportsNoBannedPlayers()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);

		var reply = await Run(MakeService(), host, match, "banlist", []);

		Assert.Equal("No banned players", reply);
	}

	[Fact]
	public async Task HandleAsync_Team_SetsTargetSlotTeam()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);

		await Run(MakeService(), host, match, "team", ["host", "blue"]);

		Assert.Equal(MatchTeam.Blue, match.Slots[0].Team);
	}

	[Fact]
	public async Task HandleAsync_Map_KnownBeatmap_UpdatesMatchMap()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);
		var mapset = new Beatmapset(1, "Artist", "Title", "creator", DateTime.UtcNow, DateTime.UtcNow);
		var bmap = new Beatmap(new string('a', 32), 500, mapset, "Version", "file.osu",
			new Difficulty(GameMode.Standard, 180, TimeSpan.FromSeconds(120), 4, 9, 8, 5, 6.5),
			new OsuBeatmapObjectCounts { MaxCombo = 500 });
		_beatmaps.FetchOneAsync(500, cancellationToken: Arg.Any<CancellationToken>()).Returns(bmap);

		var reply = await Run(MakeService(), host, match, "map", ["500"]);

		Assert.Equal(500, match.MapId);
		Assert.Contains("Title", reply);
	}

	[Fact]
	public async Task HandleAsync_Map_UnknownBeatmap_ReturnsNotFoundReply()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);
		_beatmaps.FetchOneAsync(999, cancellationToken: Arg.Any<CancellationToken>()).Returns((Beatmap?)null);

		var reply = await Run(MakeService(), host, match, "map", ["999"]);

		Assert.Equal("No beatmap with ID 999 found.", reply);
	}

	[Fact]
	public async Task HandleAsync_Mods_SetsMatchMods()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);

		await Run(MakeService(), host, match, "mods", ["HDHR"]);

		Assert.Equal(Mods.Hidden | Mods.HardRock, match.Mods);
	}

	[Fact]
	public async Task HandleAsync_Mods_NotFreemod_ReplyOmitsFreemodText()
	{
		// Freemod was never on here — the reply must not claim it was just disabled.
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);

		var reply = await Run(MakeService(), host, match, "mods", ["HD"]);

		Assert.Equal("Enabled Hidden", reply);
	}

	[Fact]
	public async Task HandleAsync_Mods_WhileFreemod_DisablesFreemodAndSetsMatchMods()
	{
		// !mp mods with a real mod token turns freemod back off — Freemod is only ever entered/exited
		// via the value passed to !mp mods, there are no separate freemods on/off command anymore.
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);
		match.Freemods = true;

		var reply = await Run(MakeService(), host, match, "mods", ["HD"]);

		Assert.False(match.Freemods);
		Assert.Equal(Mods.Hidden, match.Mods);
		Assert.Equal("Enabled Hidden, Disabled FreeMod", reply);
	}

	[Fact]
	public async Task HandleAsync_ModsFreemod_EnablesFreemodAndSplitsHostModsToSlot()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);
		match.Mods = Mods.Hidden;

		var reply = await Run(MakeService(), host, match, "mods", ["Freemod"]);

		Assert.True(match.Freemods);
		Assert.Equal(Mods.Hidden, match.Slots[0].Mods);
		Assert.Equal("Disabled Hidden, Enabled FreeMod", reply);
	}

	[Fact]
	public async Task HandleAsync_ModsNone_ClearsMods()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);
		match.Mods = Mods.Hidden | Mods.HardRock;

		await Run(MakeService(), host, match, "mods", ["None"]);

		Assert.Equal(Mods.NoMod, match.Mods);
	}

	[Fact]
	public async Task HandleAsync_Start_AlreadyInProgress_Rejected()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);
		match.InProgress = true;

		var reply = await Run(MakeService(), host, match, "start", []);

		Assert.Equal("Match is already in progress.", reply);
	}

	[Fact]
	public async Task HandleAsync_Start_NotInProgress_StartsMatch()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);

		var reply = await Run(MakeService(), host, match, "start", []);

		Assert.True(match.InProgress);
		Assert.Equal("Match started", reply);
	}

	[Fact]
	public async Task HandleAsync_Start_BeatmapMissing_NoOwnReply_MatchMembershipAnnouncesOnce()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var bot = MultiplayerTestSupport.MakePlayer(BotBootstrapService.BotId, "BasilBot");
		_fixture.RegisterAll(host, bot);
		var match = _fixture.CreateMatch(host);
		_fixture.BeatmapRepository.FetchOneAsync(100, cancellationToken: Arg.Any<CancellationToken>())
			.Returns((Beatmap?)null);
		host.Dequeue();

		var sink = new RecordingReplySink();
		var started = await MakeService().TryHandleAsync(host, match, "start", [], sink);

		Assert.False(started);
		Assert.Empty(sink.Replies);
		Assert.Empty(sink.DmReplies);
		Assert.Equal(
			ServerPacketWriter.SendMessage(bot.Name,
				"Match cannot start because the beatmap does not exist on the server.",
				"#multiplayer", bot.Id),
			host.Dequeue());
	}

	[Fact]
	public async Task HandleAsync_Abort_NotInProgress_Rejected()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);

		var reply = await Run(MakeService(), host, match, "abort", []);

		Assert.Equal("Match is not in progress.", reply);
	}

	[Fact]
	public async Task HandleAsync_Abort_InProgress_ClearsInProgressAndRound()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);
		await _fixture.MatchMembership.StartAsync(match);

		var reply = await Run(MakeService(), host, match, "abort", []);

		Assert.False(match.InProgress);
		Assert.Null(match.CurrentRoundId);
		Assert.Equal("Aborted the match", reply);
	}

	[Fact]
	public async Task HandleAsync_StartWithSeconds_DoesNotStartImmediately()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);

		var reply = await Run(MakeService(), host, match, "start", ["30"]);

		Assert.False(match.InProgress);
		var timer = match.PendingTimer;
		Assert.NotNull(timer);
		Assert.True(match.PendingTimerIsAutoStart);
		Assert.Equal("Match starts in 30 seconds", reply);

		await timer.CancelAsync();
	}

	[Fact]
	public async Task HandleAsync_Timer_SchedulesCountdownWithoutStarting()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);

		var reply = await Run(MakeService(), host, match, "timer", ["10"]);

		var timer = match.PendingTimer;
		Assert.NotNull(timer);
		Assert.False(match.InProgress);
		Assert.False(match.PendingTimerIsAutoStart);
		Assert.Equal("Countdown started: 10 seconds", reply);

		await timer.CancelAsync();
	}

	[Fact]
	public async Task HandleAsync_Timer_NoArgs_DefaultsTo30Seconds()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);

		var reply = await Run(MakeService(), host, match, "timer", []);

		Assert.Equal("Countdown started: 30 seconds", reply);

		var timer = match.PendingTimer;
		Assert.NotNull(timer);
		await timer.CancelAsync();
	}

	[Fact]
	public async Task HandleAsync_AbortTimer_NoPendingTimer_ReturnsMessage()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);

		var reply = await Run(MakeService(), host, match, "aborttimer", []);

		Assert.Equal("No countdown is running.", reply);
	}

	[Fact]
	public async Task HandleAsync_AbortTimer_CancelsPendingCountdown()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);
		var service = MakeService();
		await Run(service, host, match, "start", ["30"]);
		var cts = match.PendingTimer;

		var reply = await Run(service, host, match, "aborttimer", []);

		Assert.Null(match.PendingTimer);
		Assert.False(match.PendingTimerIsAutoStart);
		Assert.True(cts!.IsCancellationRequested);
		Assert.Equal("Countdown aborted", reply);
	}

	[Theory]
	[InlineData("size", new[] { "5" })]
	[InlineData("set", new[] { "2" })]
	[InlineData("team", new[] { "guest", "red" })]
	public async Task HandleAsync_GameplaySettingChange_CancelsQueuedAutoStart(string subcommand, string[] args)
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var guest = MultiplayerTestSupport.MakePlayer(2, "guest");
		var bot = new GameSession(BotBootstrapService.BotId, "BasilBot", "bot-token",
				UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
			{ IsBot = true };
		_fixture.RegisterAll(host, guest, bot);
		var match = _fixture.CreateMatch(host);
		await _fixture.MatchMembership.JoinAsync(guest, match, "");
		var service = MakeService();
		await Run(service, host, match, "start", ["30"]);
		var cts = match.PendingTimer;
		host.Dequeue();

		await Run(service, host, match, subcommand, args);

		Assert.Null(match.PendingTimer);
		Assert.False(match.PendingTimerIsAutoStart);
		Assert.True(cts!.IsCancellationRequested);
		Assert.Contains(
			ServerPacketWriter.SendMessage(bot.Name, "Match start cancelled — room settings changed.",
				"#multiplayer", bot.Id),
			MultiplayerTestSupport.Chunk(host.Dequeue()));
	}

	[Fact]
	public async Task HandleAsync_MapChange_CancelsQueuedAutoStart()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var bot = new GameSession(BotBootstrapService.BotId, "BasilBot", "bot-token",
				UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
			{ IsBot = true };
		_fixture.RegisterAll(host, bot);
		var match = _fixture.CreateMatch(host);
		var bmap = MultiplayerTestSupport.MakeBeatmap(200);
		_beatmaps.FetchOneAsync(200, cancellationToken: Arg.Any<CancellationToken>()).Returns(bmap);
		var service = MakeService();
		await Run(service, host, match, "start", ["30"]);

		await Run(service, host, match, "map", ["200"]);

		Assert.Null(match.PendingTimer);
		Assert.False(match.PendingTimerIsAutoStart);
	}

	[Theory]
	[InlineData("name", new[] { "renamed" })]
	[InlineData("password", new[] { "secret" })]
	public async Task HandleAsync_NonGameplaySettingChange_LeavesQueuedAutoStartRunning(string subcommand,
		string[] args)
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);
		var service = MakeService();
		await Run(service, host, match, "start", ["30"]);
		var cts = match.PendingTimer;

		await Run(service, host, match, subcommand, args);

		Assert.Same(cts, match.PendingTimer);
		Assert.True(match.PendingTimerIsAutoStart);

		await match.PendingTimer!.CancelAsync();
	}

	/// <summary>
	///     Diagnostic/regression test for the real end-to-end announce pipeline.
	/// </summary>
	[Fact]
	public async Task HandleAsync_Timer_AnnouncesQueuedAndFinishedMessagesToMatchChannel()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var bot = new GameSession(BotBootstrapService.BotId, "BasilBot", "bot-token",
				UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
			{ IsBot = true };
		_fixture.RegisterAll(host, bot);
		var match = _fixture.CreateMatch(host);
		host.Dequeue();

		await Run(MakeService(), host, match, "timer", ["2"]);

		var queuedPacket = ServerPacketWriter.SendMessage("BasilBot", "Started a 2-second countdown.",
			"#multiplayer", BotBootstrapService.BotId);
		Assert.Contains(queuedPacket, MultiplayerTestSupport.Chunk(host.Dequeue()));

		await Task.Delay(TimeSpan.FromSeconds(2.5));

		var finishedPacket = ServerPacketWriter.SendMessage("BasilBot", "Countdown finished",
			"#multiplayer", BotBootstrapService.BotId);
		Assert.Contains(finishedPacket, MultiplayerTestSupport.Chunk(host.Dequeue()));
	}

	[Theory]
	[InlineData(5, new[] { 4, 3 })]
	[InlineData(3, new int[0])]
	[InlineData(45, new[] { 30, 10, 5, 4, 3 })]
	// 60 is within the 5s near-total ignore window (61-60=1) — announcing it right after
	// "Queued...61 seconds" would be redundant, so it's dropped.
	[InlineData(61, new[] { 30, 10, 5, 4, 3 })]
	[InlineData(65, new[] { 30, 10, 5, 4, 3 })]
	// Long countdowns get an extra reminder every 60s on top of the fixed marks.
	[InlineData(300, new[] { 240, 180, 120, 60, 30, 10, 5, 4, 3 })]
	public void ComputeAnnounceCheckpoints_AutoStart_StopsAt3(int total, int[] expected)
	{
		var result = MpCommandService.ComputeAnnounceCheckpoints(total);

		Assert.Equal(expected, result);
	}

	[Theory]
	[InlineData(15, new[] { 10, 5 })]
	[InlineData(45, new[] { 30, 10, 5 })]
	[InlineData(300, new[] { 240, 180, 120, 60, 30, 10, 5 })]
	public void ComputeAnnounceCheckpoints_Timer_SkipsFastFinalTick(int total, int[] expected)
	{
		var result = MpCommandService.ComputeAnnounceCheckpoints(total, false);

		Assert.Equal(expected, result);
	}

	[Fact]
	public async Task HandleAsync_Kick_HostNotReferee_Succeeds()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var referee = MultiplayerTestSupport.MakePlayer(2, "referee");
		_fixture.RegisterAll(host, referee);
		var match = _fixture.CreateMatch(host, hostIsReferee: false);
		match.AddReferee(referee.Id);
		_users.FetchByNameAsync("host", Arg.Any<CancellationToken>()).Returns(MakeUser(host.Id, "host"));

		var reply = await Run(MakeService(), referee, match, "kick", ["host"]);

		Assert.Null(host.Match);
		Assert.Equal("Kicked host from the match", reply);
	}

	[Fact]
	public async Task HandleAsync_Kick_RefereeTarget_Rejected()
	{
		// Referees are protected from kick/ban outright — remove referee status first with
		// !mp removeref (see MatchControlService.KickResult.TargetIsReferee).
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var referee = MultiplayerTestSupport.MakePlayer(2, "referee");
		_fixture.RegisterAll(host, referee);
		var match = _fixture.CreateMatch(host);
		await _fixture.MatchMembership.JoinAsync(referee, match, "");
		match.AddReferee(referee.Id);
		_users.FetchByNameAsync("referee", Arg.Any<CancellationToken>()).Returns(MakeUser(referee.Id, "referee"));

		var reply = await Run(MakeService(), host, match, "kick", ["referee"]);

		Assert.Same(match, referee.Match);
		Assert.Contains(referee.Id, match.Referees);
		Assert.Contains("Remove referee status first", reply);
	}

	[Fact]
	public async Task HandleAsync_Kick_NonHostTarget_RemovesFromMatch()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var other = MultiplayerTestSupport.MakePlayer(2, "other");
		_fixture.RegisterAll(host, other);
		var match = _fixture.CreateMatch(host);
		await _fixture.MatchMembership.JoinAsync(other, match, "");
		_users.FetchByNameAsync("other", Arg.Any<CancellationToken>()).Returns(MakeUser(other.Id, "other"));

		var reply = await Run(MakeService(), host, match, "kick", ["other"]);

		Assert.Null(other.Match);
		Assert.Equal("Kicked other from the match", reply);
	}

	[Fact]
	public async Task HandleAsync_Ban_PresentPlayer_KicksAndPreventsRejoin()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var other = MultiplayerTestSupport.MakePlayer(2, "other");
		_fixture.RegisterAll(host, other);
		var match = _fixture.CreateMatch(host);
		await _fixture.MatchMembership.JoinAsync(other, match, "");
		_users.FetchByNameAsync("other", Arg.Any<CancellationToken>()).Returns(MakeUser(other.Id, "other"));

		var reply = await Run(MakeService(), host, match, "ban", ["other"]);

		Assert.Null(other.Match);
		Assert.Contains(other.Id, match.BannedIds);
		Assert.Equal("Banned other from the match", reply);

		var rejoined = await _fixture.MatchMembership.JoinAsync(other, match, "");
		Assert.NotEqual(MatchMembershipService.JoinResult.Ok, rejoined);
	}

	[Fact]
	public async Task HandleAsync_Ban_NotInMatch_StillBansByUserId()
	{
		// Ban applies to a UserId regardless of physical presence — no online/in-match requirement.
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var other = MultiplayerTestSupport.MakePlayer(2, "other");
		_fixture.RegisterAll(host, other);
		var match = _fixture.CreateMatch(host);
		_users.FetchByNameAsync("other", Arg.Any<CancellationToken>()).Returns(MakeUser(other.Id, "other"));

		var reply = await Run(MakeService(), host, match, "ban", ["other"]);

		Assert.Contains(other.Id, match.BannedIds);
		Assert.Equal("Banned other from the match", reply);

		var rejoined = await _fixture.MatchMembership.JoinAsync(other, match, "");
		Assert.NotEqual(MatchMembershipService.JoinResult.Ok, rejoined);
	}

	[Fact]
	public async Task HandleAsync_Ban_UnknownUser_Rejected()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);
		_users.FetchByNameAsync("nobody", Arg.Any<CancellationToken>()).Returns((User?)null);

		var reply = await Run(MakeService(), host, match, "ban", ["nobody"]);

		Assert.Equal("User is not registered.", reply);
	}

	[Fact]
	public async Task HandleAsync_Ban_RefereeTarget_Rejected()
	{
		// Referees are protected from kick/ban outright — remove referee status first with
		// !mp removeref (see MatchControlService.BanResult.TargetIsReferee).
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var referee = MultiplayerTestSupport.MakePlayer(2, "referee");
		_fixture.RegisterAll(host, referee);
		var match = _fixture.CreateMatch(host);
		await _fixture.MatchMembership.JoinAsync(referee, match, "");
		match.AddReferee(referee.Id);
		_users.FetchByNameAsync("referee", Arg.Any<CancellationToken>()).Returns(MakeUser(referee.Id, "referee"));

		var reply = await Run(MakeService(), host, match, "ban", ["referee"]);

		Assert.DoesNotContain(referee.Id, match.BannedIds);
		Assert.Contains(referee.Id, match.Referees);
		Assert.Same(match, referee.Match);
		Assert.Contains("Remove referee status first", reply);
	}

	[Fact]
	public async Task HandleAsync_Unban_OfflinePlayer_RemovesFromBannedIds()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);
		match.AddBan(99);
		_users.FetchByNameAsync("offline_guy", Arg.Any<CancellationToken>()).Returns(MakeUser(99, "offline_guy"));

		var reply = await Run(MakeService(), host, match, "unban", ["offline_guy"]);

		Assert.DoesNotContain(99, match.BannedIds);
		Assert.Equal("Unbanned offline_guy from the match", reply);
	}

	[Fact]
	public async Task HandleAsync_Unban_ThenRejoin_Succeeds()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var other = MultiplayerTestSupport.MakePlayer(2, "other");
		_fixture.RegisterAll(host, other);
		var match = _fixture.CreateMatch(host);
		match.AddBan(other.Id);
		_users.FetchByNameAsync("other", Arg.Any<CancellationToken>()).Returns(MakeUser(other.Id, "other"));

		await Run(MakeService(), host, match, "unban", ["other"]);
		var rejoined = await _fixture.MatchMembership.JoinAsync(other, match, "");

		Assert.Equal(MatchMembershipService.JoinResult.Ok, rejoined);
	}

	[Fact]
	public async Task HandleAsync_Unban_NotBanned_ReturnsNotBannedMessage()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);
		_users.FetchByNameAsync("other", Arg.Any<CancellationToken>()).Returns(MakeUser(2, "other"));

		var reply = await Run(MakeService(), host, match, "unban", ["other"]);

		Assert.Equal("other is not banned from this match.", reply);
	}

	[Fact]
	public async Task HandleAsync_Close_TearsDownRegardlessOfOccupancy()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var other = MultiplayerTestSupport.MakePlayer(2, "other");
		_fixture.RegisterAll(host, other);
		var match = _fixture.CreateMatch(host);
		await _fixture.MatchMembership.JoinAsync(other, match, "");

		var reply = await Run(MakeService(), host, match, "close", []);

		Assert.Null(_fixture.MatchRegistry.GetById(match.Id));
		Assert.Null(host.Match);
		Assert.Null(other.Match);
		Assert.Equal("Closed the match", reply);
	}

	[Fact]
	public async Task HandleAsync_Set_ChangesTeamsConditionAndSize()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);

		var reply = await Run(MakeService(), host, match, "set", ["2", "1", "8"]);

		Assert.Equal(MatchTeamType.TeamVs, match.TeamType);
		Assert.Equal(MatchWinCondition.Accuracy, match.WinCondition);
		Assert.Equal(SlotStatus.Locked, match.Slots[8].Status);
		Assert.Equal("Changed match settings to TeamVs, Accuracy, 8 slots.", reply);
	}

	[Fact]
	public async Task HandleAsync_Set_TeammodeOnly_LeavesScoremodeAndSizeUnchanged()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);
		match.WinCondition = MatchWinCondition.Combo;

		var reply = await Run(MakeService(), host, match, "set", ["2"]);

		Assert.Equal(MatchTeamType.TeamVs, match.TeamType);
		Assert.Equal(MatchWinCondition.Combo, match.WinCondition);
		Assert.Equal("Changed match settings to TeamVs, Combo.", reply);
	}

	[Fact]
	public async Task HandleAsync_Set_InvalidNumericArg_ReturnsUsage()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);

		var reply = await Run(MakeService(), host, match, "set", ["4", "0", "8"]);

		Assert.Equal("Usage: !mp set <teammode 0-3> [scoremode 0-3] [size 1-16]", reply);
	}

	[Fact]
	public async Task HandleAsync_Set_SizeOutOfRange_Clamps()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);

		var reply = await Run(MakeService(), host, match, "set", ["0", "0", "99"]);

		Assert.Equal(SlotStatus.Open, match.Slots[15].Status);
		Assert.Equal("Changed match settings to HeadToHead, Score, 16 slots.", reply);
	}

	[Fact]
	public async Task MakeAsync_CreatesMatch_JoinsCreatorAsHostAndReferee()
	{
		var sender = MultiplayerTestSupport.MakePlayer(1, "creator");
		_fixture.RegisterAll(sender);

		var reply = await RunMake(MakeService(), sender, ["My", "Tournament"]);

		Assert.Same(sender.Match, _fixture.MatchRegistry.All.Single());
		Assert.Equal(sender.Id, sender.Match!.HostId);
		Assert.Contains(sender.Id, sender.Match.Referees);
		Assert.Equal("My Tournament", sender.Match.Name);
		Assert.Contains("Created the match", reply);
	}

	[Fact]
	public async Task MakeAsync_IsPrivateTrue_CreatesPrivateMatch()
	{
		var sender = MultiplayerTestSupport.MakePlayer(1, "creator");
		_fixture.RegisterAll(sender);

		var reply = await RunMake(MakeService(), sender, ["Room"], isPrivate: true);

		Assert.True(sender.Match!.IsPrivate);
		Assert.Contains("(private)", reply);
	}

	[Fact]
	public async Task MakeAsync_IsPrivateFalse_CreatesPublicMatch()
	{
		var sender = MultiplayerTestSupport.MakePlayer(1, "creator");
		_fixture.RegisterAll(sender);

		await RunMake(MakeService(), sender, ["Room"]);

		Assert.False(sender.Match!.IsPrivate);
	}

	[Fact]
	public async Task MakeAsync_AllPlayersLeave_DoesNotTearDownWhileRefereesRemain()
	{
		// Reversed from the room's normal all-slots-empty auto-teardown: a `!mp make` room persists
		// until `!mp close` or its referee list empties, regardless of userSession occupancy.
		var sender = MultiplayerTestSupport.MakePlayer(1, "creator");
		_fixture.RegisterAll(sender);
		await RunMake(MakeService(), sender, ["Room"]);
		var match = sender.Match!;

		await _fixture.MatchMembership.LeaveAsync(sender, match);

		Assert.NotNull(_fixture.MatchRegistry.GetById(match.Id));
		Assert.Contains(sender.Id, match.Referees);
	}

	[Fact]
	public async Task RemoveRef_LastReferee_RejectedAndMatchStaysOpen()
	{
		var sender = MultiplayerTestSupport.MakePlayer(1, "creator");
		_fixture.RegisterAll(sender);
		var service = MakeService();
		await RunMake(service, sender, ["Room"]);
		var match = sender.Match!;

		var reply = await Run(service, sender, match, "removeref", ["creator"]);

		Assert.NotNull(_fixture.MatchRegistry.GetById(match.Id));
		Assert.Contains(sender.Id, match.Referees);
		Assert.Contains("at least one referee must remain", reply);
	}

	[Fact]
	public async Task RemoveRef_NotLastReferee_Removes()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var referee = MultiplayerTestSupport.MakePlayer(2, "referee");
		_fixture.RegisterAll(host, referee);
		var match = _fixture.CreateMatch(host, hostIsReferee: false);
		match.AddReferee(host.Id);
		match.AddReferee(referee.Id);
		var service = MakeService();

		await Run(service, host, match, "removeref", ["referee"]);

		Assert.NotNull(_fixture.MatchRegistry.GetById(match.Id));
		Assert.DoesNotContain(referee.Id, match.Referees);
	}

	[Fact]
	public async Task MakeAsync_SetsSenderScopeToNewMatch()
	{
		var sender = MultiplayerTestSupport.MakePlayer(1, "creator");
		_fixture.RegisterAll(sender);

		var reply = await RunMake(MakeService(), sender, ["Room"]);

		Assert.Equal(sender.Match!.DbId, sender.MpScopeMatchId);
		Assert.Contains("scoped to this match", reply);
	}

	[Fact]
	public void SetScopeAsync_NoArgs_NoScope_ReportsNotScoped()
	{
		var sender = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(sender);

		var reply = RunSetScope(MakeService(), sender, []);

		Assert.Equal("You're not scoped to any match.", reply);
	}

	[Fact]
	public void SetScopeAsync_NoArgs_WithScope_ReportsCurrentMatch()
	{
		var sender = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(sender);
		var match = _fixture.CreateMatch(sender);
		sender.MpScopeMatchId = match.DbId;

		var reply = RunSetScope(MakeService(), sender, []);

		Assert.Contains($"#{match.DbId}", reply);
	}

	[Fact]
	public void SetScopeAsync_RefereeOfOtherMatch_SetsScope()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var referee = MultiplayerTestSupport.MakePlayer(2, "ref");
		_fixture.RegisterAll(host, referee);
		var match = _fixture.CreateMatch(host);
		match.AddReferee(referee.Id);

		var reply = RunSetScope(MakeService(), referee, [match.DbId.ToString()]);

		Assert.Equal(match.DbId, referee.MpScopeMatchId);
		Assert.Contains($"#{match.DbId}", reply);
	}

	[Fact]
	public void SetScopeAsync_NotReferee_Rejected()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var other = MultiplayerTestSupport.MakePlayer(2, "other");
		_fixture.RegisterAll(host, other);
		var match = _fixture.CreateMatch(host);

		var reply = RunSetScope(MakeService(), other, [match.DbId.ToString()]);

		Assert.Null(other.MpScopeMatchId);
		Assert.Contains("not a referee", reply);
	}

	[Fact]
	public void SetScopeAsync_UnknownMatchId_Rejected()
	{
		var sender = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(sender);

		var reply = RunSetScope(MakeService(), sender, ["999"]);

		Assert.Contains("No active match", reply);
	}

	[Fact]
	public async Task HandleAsync_Private_NoArgsNonReferee_StillReportsStatus()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var other = MultiplayerTestSupport.MakePlayer(2, "other");
		_fixture.RegisterAll(host, other);
		var match = _fixture.CreateMatch(host);

		// "!mp private" with no argument only reports status — read-only, so open to non-referees too.
		var reply = await Run(MakeService(), other, match, "private", []);

		Assert.Contains("not private", reply);
	}

	[Fact]
	public async Task HandleAsync_Private_WithArgNonReferee_SilentlyIgnored()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var other = MultiplayerTestSupport.MakePlayer(2, "other");
		_fixture.RegisterAll(host, other);
		var match = _fixture.CreateMatch(host);

		// "!mp private 1" mutates state — still referee-gated.
		var reply = await Run(MakeService(), other, match, "private", ["1"]);

		Assert.Null(reply);
	}

	[Fact]
	public async Task HandleAsync_Private_Referee_ShowsStatus()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);

		var reply = await Run(MakeService(), host, match, "private", []);

		Assert.Contains("not private", reply);
	}

	[Fact]
	public async Task HandleAsync_Private_Referee_SetsTrue()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);

		var reply = await Run(MakeService(), host, match, "private", ["1"]);

		Assert.True(match.IsPrivate);
		Assert.Contains("now private", reply);
	}

	[Fact]
	public async Task HandleAsync_Private_Referee_SetsFalse()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);
		match.IsPrivate = true;

		var reply = await Run(MakeService(), host, match, "private", ["0"]);

		Assert.False(match.IsPrivate);
		Assert.Contains("now public", reply);
	}

	[Fact]
	public async Task JoinAsync_NonExistent_ReturnsError()
	{
		var sender = MultiplayerTestSupport.MakePlayer(1, "userSession");
		_fixture.RegisterAll(sender);

		var reply = await RunJoin(MakeService(), sender, ["999"]);

		Assert.Contains("No active match", reply);
	}

	[Fact]
	public async Task JoinAsync_Private_Fails()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var player = MultiplayerTestSupport.MakePlayer(2, "userSession");
		_fixture.RegisterAll(host, player);
		var match = _fixture.CreateMatch(host);
		match.IsPrivate = true;

		var reply = await RunJoin(MakeService(), player, [match.DbId.ToString()]);

		Assert.Contains("private", reply);
	}

	[Fact]
	public async Task JoinAsync_Normal_Succeeds()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var player = MultiplayerTestSupport.MakePlayer(2, "userSession");
		_fixture.RegisterAll(host, player);
		var match = _fixture.CreateMatch(host);

		var reply = await RunJoin(MakeService(), player, [match.DbId.ToString()]);

		Assert.NotNull(reply);
		Assert.Contains("Joined match", reply);
	}

	private static User MakeUser(int id, string name)
	{
		return new User(id, name, Country.Xx, UserPrivileges.Unrestricted, default);
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