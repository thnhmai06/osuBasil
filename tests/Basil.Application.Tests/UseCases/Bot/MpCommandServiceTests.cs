using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Users;
using Basil.Application.Services.Bot;
using Basil.Application.Sessions;
using Basil.Application.Tests.PacketHandlers;
using Basil.Domain.Beatmaps;
using Basil.Domain.Login;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using NSubstitute;

namespace Basil.Application.Tests.UseCases.Bot;

/// <summary>
///     Every subcommand except "help" requires MatchSession.IsReferee — that gate, plus a
///     representative sample of the wrapper-over-existing-MatchMembershipService commands, is what's
///     covered here. Not every argument-validation branch is exercised (they're all short, obvious
///     `return "Usage: ..."` guards).
/// </summary>
public class MpCommandServiceTests
{
    private readonly MultiplayerTestSupport.Fixture _fixture = new();
    private readonly IMapRepository _maps = Substitute.For<IMapRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();

    private MpCommandService MakeService()
    {
        return new MpCommandService(_fixture.MatchMembership, _fixture.MatchRegistry, _fixture.MatchPersistence, _maps,
            _fixture.SessionRegistry, _users);
    }

    [Fact]
    public async Task HandleAsync_NonReferee_SilentlyIgnored()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        var other = MultiplayerTestSupport.MakePlayer(2, "other");
        _fixture.RegisterAll(host, other);
        var match = _fixture.CreateMatch(host);

        var reply = await MakeService().HandleAsync(other, match, "lock", ["1"]);

        Assert.Null(reply);
    }

    [Fact]
    public async Task HandleAsync_Help_AnyMatchMember_ReturnsHelpText()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        var other = MultiplayerTestSupport.MakePlayer(2, "other");
        _fixture.RegisterAll(host, other);
        var match = _fixture.CreateMatch(host);

        var reply = await MakeService().HandleAsync(other, match, "help", []);

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

        var reply = await MakeService().HandleAsync(host, match, "lock", []);

        Assert.True(match.IsLocked);
        Assert.Equal("Locked the match", reply);
        Assert.False(await _fixture.MatchMembership.Join(other, match, ""));
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

        var reply = await MakeService().HandleAsync(host, match, "unlock", []);

        Assert.False(match.IsLocked);
        Assert.Equal("Unlocked the match", reply);
        Assert.True(await _fixture.MatchMembership.Join(other, match, ""));
    }

    [Fact]
    public async Task HandleAsync_Size_LocksSlotsBeyondLimit()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);

        await MakeService().HandleAsync(host, match, "size", ["4"]);

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

        var reply = await MakeService().HandleAsync(host, match, "size", [arg]);

        Assert.Equal($"Changed match to size {expectedSize}", reply);
    }

    [Fact]
    public async Task HandleAsync_Move_RelocatesTargetToOpenSlot()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        var other = MultiplayerTestSupport.MakePlayer(2, "other");
        _fixture.RegisterAll(host, other);
        var match = _fixture.CreateMatch(host);
        await _fixture.MatchMembership.Join(other, match, "");

        var reply = await MakeService().HandleAsync(host, match, "move", ["other", "5"]);

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
        await _fixture.MatchMembership.Join(other, match, "");

        var reply = await MakeService().HandleAsync(host, match, "move", ["other", "99"]);

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
        await _fixture.MatchMembership.Join(other, match, "");

        var reply = await MakeService().HandleAsync(host, match, "host", ["other"]);

        Assert.Equal(2, match.HostId);
        Assert.Equal("Changed match host to other", reply);
    }

    [Fact]
    public async Task HandleAsync_ClearHost_SetsHostIdToZero()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);

        await MakeService().HandleAsync(host, match, "clearhost", []);

        Assert.Equal(0, match.HostId);
    }

    [Fact]
    public async Task HandleAsync_Name_RenamesMatch()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);

        await MakeService().HandleAsync(host, match, "name", ["New", "Name"]);

        Assert.Equal("New Name", match.Name);
    }

    [Fact]
    public async Task HandleAsync_PasswordNoArgs_ClearsPassword()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);
        match.Password = "secret";

        await MakeService().HandleAsync(host, match, "password", []);

        Assert.Equal("", match.Password);
    }

    [Fact]
    public async Task HandleAsync_PasswordOff_IsTreatedAsLiteralText()
    {
        // "off"/"none" are no longer special keywords — only omitting the arg clears the password.
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);

        await MakeService().HandleAsync(host, match, "password", ["off"]);

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

        var reply = await MakeService().HandleAsync(referee, match, "addref", ["other"]);

        Assert.Contains(other.Id, match.Referees);
        Assert.Equal("Added other to the match referees", reply);
    }

    [Fact]
    public async Task HandleAsync_HostNotAddedAsReferee_CommandsSilentlyIgnored()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host, hostIsReferee: false);

        var reply = await MakeService().HandleAsync(host, match, "settings", []);

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
        _maps.FetchOneAsync(id: match.MapId, cancellationToken: Arg.Any<CancellationToken>()).Returns(bmap);

        var reply = await MakeService().HandleAsync(host, match, "settings", []);

        Assert.Contains($"Beatmap: {bmap.Id} {bmap.FullName}", reply);
    }

    [Fact]
    public async Task HandleAsync_Settings_BeatmapMissingFromDb_ShowsNotFound()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);
        _maps.FetchOneAsync(id: match.MapId, cancellationToken: Arg.Any<CancellationToken>()).Returns((Beatmap?)null);

        var reply = await MakeService().HandleAsync(host, match, "settings", []);

        Assert.Contains("Beatmap: Not found", reply);
    }

    [Fact]
    public async Task HandleAsync_AddRefThenListRefs_ReflectsAddition()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        var other = MultiplayerTestSupport.MakePlayer(2, "other");
        _fixture.RegisterAll(host, other);
        var match = _fixture.CreateMatch(host);
        var service = MakeService();

        await service.HandleAsync(host, match, "addref", ["other"]);
        var listing = await service.HandleAsync(host, match, "listrefs", []);

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

        await MakeService().HandleAsync(host, match, "removeref", ["other"]);

        Assert.DoesNotContain(other.Id, match.Referees);
    }

    [Fact]
    public async Task HandleAsync_BanList_ListsBannedNames()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);
        match.AddBan(2);
        _users.FetchByIdAsync(2, Arg.Any<CancellationToken>()).Returns(MakeUser(2, "banned_guy"));

        var reply = await MakeService().HandleAsync(host, match, "banlist", []);

        Assert.Contains("banned_guy", reply);
    }

    [Fact]
    public async Task HandleAsync_BanList_Empty_ReportsNoBannedPlayers()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);

        var reply = await MakeService().HandleAsync(host, match, "banlist", []);

        Assert.Equal("No banned players", reply);
    }

    [Fact]
    public async Task HandleAsync_Team_SetsTargetSlotTeam()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);

        await MakeService().HandleAsync(host, match, "team", ["host", "blue"]);

        Assert.Equal(MatchTeam.Blue, match.Slots[0].Team);
    }

    [Fact]
    public async Task HandleAsync_Map_KnownBeatmap_UpdatesMatchMap()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);
        var mapset = new Mapset(1, "Artist", "Title", "creator", DateTime.UtcNow, DateTime.UtcNow);
        var bmap = new Beatmap(new string('a', 32), 500, mapset, "Version", "file.osu", TimeSpan.FromSeconds(120),
            500, 0, 0, new Difficulty(GameMode.Standard, 180, 4, 9, 8, 5, 6.5), new Dictionary<string, int>());
        _maps.FetchOneAsync(500, cancellationToken: Arg.Any<CancellationToken>()).Returns(bmap);

        var reply = await MakeService().HandleAsync(host, match, "map", ["500"]);

        Assert.Equal(500, match.MapId);
        Assert.Contains("Title", reply);
    }

    [Fact]
    public async Task HandleAsync_Map_UnknownBeatmap_ReturnsNotFoundReply()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);
        _maps.FetchOneAsync(999, cancellationToken: Arg.Any<CancellationToken>()).Returns((Beatmap?)null);

        var reply = await MakeService().HandleAsync(host, match, "map", ["999"]);

        Assert.Equal("No beatmap with id 999 found locally.", reply);
    }

    [Fact]
    public async Task HandleAsync_Mods_SetsMatchMods()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);

        await MakeService().HandleAsync(host, match, "mods", ["HDHR"]);

        Assert.Equal(Mods.Hidden | Mods.HardRock, match.Mods);
    }

    [Fact]
    public async Task HandleAsync_Mods_NotFreemod_ReplyOmitsFreemodText()
    {
        // Freemod was never on here — the reply must not claim it was just disabled.
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);

        var reply = await MakeService().HandleAsync(host, match, "mods", ["HD"]);

        Assert.Equal("Enabled Hidden", reply);
    }

    [Fact]
    public async Task HandleAsync_Mods_WhileFreemod_DisablesFreemodAndSetsMatchMods()
    {
        // !mp mods with a real mod token turns freemod back off — Freemod is only ever entered/exited
        // via the value passed to !mp mods, there's no separate freemods on/off command anymore.
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);
        match.Freemods = true;

        var reply = await MakeService().HandleAsync(host, match, "mods", ["HD"]);

        Assert.False(match.Freemods);
        Assert.Equal(Mods.Hidden, match.Mods);
        Assert.Equal("Enabled Hidden, disabled FreeMod", reply);
    }

    [Fact]
    public async Task HandleAsync_ModsFreemod_EnablesFreemodAndSplitsHostModsToSlot()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);
        match.Mods = Mods.Hidden;

        var reply = await MakeService().HandleAsync(host, match, "mods", ["Freemod"]);

        Assert.True(match.Freemods);
        Assert.Equal(Mods.Hidden, match.Slots[0].Mods);
        Assert.Equal("Enabled FreeMod", reply);
    }

    [Fact]
    public async Task HandleAsync_ModsNone_ClearsMods()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);
        match.Mods = Mods.Hidden | Mods.HardRock;

        await MakeService().HandleAsync(host, match, "mods", ["None"]);

        Assert.Equal(Mods.NoMod, match.Mods);
    }

    [Fact]
    public async Task HandleAsync_Start_AlreadyInProgress_Rejected()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);
        match.InProgress = true;

        var reply = await MakeService().HandleAsync(host, match, "start", []);

        Assert.Equal("Match is already in progress.", reply);
    }

    [Fact]
    public async Task HandleAsync_Start_NotInProgress_StartsMatch()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);

        var reply = await MakeService().HandleAsync(host, match, "start", []);

        Assert.True(match.InProgress);
        Assert.Equal("Match started", reply);
    }

    [Fact]
    public async Task HandleAsync_Abort_NotInProgress_Rejected()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);

        var reply = await MakeService().HandleAsync(host, match, "abort", []);

        Assert.Equal("Match is not in progress.", reply);
    }

    [Fact]
    public async Task HandleAsync_Abort_InProgress_ClearsInProgressAndRound()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);
        await _fixture.MatchMembership.StartAsync(match);

        var reply = await MakeService().HandleAsync(host, match, "abort", []);

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

        var reply = await MakeService().HandleAsync(host, match, "start", ["30"]);

        Assert.False(match.InProgress);
        Assert.NotNull(match.PendingTimer);
        Assert.True(match.PendingTimerIsAutoStart);
        Assert.Equal("Match starts in 30 seconds", reply);

        await match.PendingTimer?.CancelAsync();
    }

    [Fact]
    public async Task HandleAsync_Timer_SchedulesCountdownWithoutStarting()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);

        var reply = await MakeService().HandleAsync(host, match, "timer", ["10"]);

        Assert.NotNull(match.PendingTimer);
        Assert.False(match.InProgress);
        Assert.False(match.PendingTimerIsAutoStart);
        Assert.Equal("Countdown started: 10 seconds", reply);

        await match.PendingTimer?.CancelAsync();
    }

    [Fact]
    public async Task HandleAsync_Timer_NoArgs_DefaultsTo30Seconds()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);

        var reply = await MakeService().HandleAsync(host, match, "timer", []);

        Assert.Equal("Countdown started: 30 seconds", reply);

        await match.PendingTimer?.CancelAsync();
    }

    [Fact]
    public async Task HandleAsync_AbortTimer_NoPendingTimer_ReturnsMessage()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);

        var reply = await MakeService().HandleAsync(host, match, "aborttimer", []);

        Assert.Equal("No countdown is running.", reply);
    }

    [Fact]
    public async Task HandleAsync_AbortTimer_CancelsPendingCountdown()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);
        var service = MakeService();
        await service.HandleAsync(host, match, "start", ["30"]);
        var cts = match.PendingTimer;

        var reply = await service.HandleAsync(host, match, "aborttimer", []);

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
        var bot = new PlayerSession(BotBootstrapService.BotId, "BasilBot", "bot-token",
            UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch) { IsBot = true };
        _fixture.RegisterAll(host, guest, bot);
        var match = _fixture.CreateMatch(host);
        await _fixture.MatchMembership.Join(guest, match, "");
        var service = MakeService();
        await service.HandleAsync(host, match, "start", ["30"]);
        var cts = match.PendingTimer;
        host.Dequeue();

        await service.HandleAsync(host, match, subcommand, args);

        Assert.Null(match.PendingTimer);
        Assert.False(match.PendingTimerIsAutoStart);
        Assert.True(cts!.IsCancellationRequested);
        Assert.Contains(
            ServerPacketWriter.SendMessage(bot.Name, "Match start cancelled — room settings changed.",
                match.ChatChannelName, bot.Id),
            MultiplayerTestSupport.Chunk(host.Dequeue()));
    }

    [Fact]
    public async Task HandleAsync_MapChange_CancelsQueuedAutoStart()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        var bot = new PlayerSession(BotBootstrapService.BotId, "BasilBot", "bot-token",
            UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch) { IsBot = true };
        _fixture.RegisterAll(host, bot);
        var match = _fixture.CreateMatch(host);
        var bmap = MultiplayerTestSupport.MakeBeatmap(200);
        _maps.FetchOneAsync(200, cancellationToken: Arg.Any<CancellationToken>()).Returns(bmap);
        var service = MakeService();
        await service.HandleAsync(host, match, "start", ["30"]);

        await service.HandleAsync(host, match, "map", ["200"]);

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
        await service.HandleAsync(host, match, "start", ["30"]);
        var cts = match.PendingTimer;

        await service.HandleAsync(host, match, subcommand, args);

        Assert.Same(cts, match.PendingTimer);
        Assert.True(match.PendingTimerIsAutoStart);

        await match.PendingTimer!.CancelAsync();
    }

    /// <summary>
    ///     Diagnostic/regression test for the real end-to-end announce pipeline (checkpoint computation
    ///     alone is covered by <see cref="ComputeAnnounceCheckpoints_ReturnsExpectedMarks" />, but every
    ///     other timer test cancels the countdown immediately, so nothing exercises whether
    ///     <see cref="Basil.Application.Services.Multiplayer.MatchControlService" />'s fire-and-forget task actually reaches
    ///     the match channel with a real bot session registered).
    /// </summary>
    [Fact]
    public async Task HandleAsync_Timer_AnnouncesQueuedAndFinishedMessagesToMatchChannel()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        var bot = new PlayerSession(BotBootstrapService.BotId, "BasilBot", "bot-token",
            UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch) { IsBot = true };
        _fixture.RegisterAll(host, bot);
        var match = _fixture.CreateMatch(host);
        host.Dequeue();

        await MakeService().HandleAsync(host, match, "timer", ["2"]);

        var queuedPacket = ServerPacketWriter.SendMessage("BasilBot", "Queued the match to start in 2 seconds",
            match.ChatChannelName, BotBootstrapService.BotId);
        Assert.Contains(queuedPacket, MultiplayerTestSupport.Chunk(host.Dequeue()));

        await Task.Delay(TimeSpan.FromSeconds(2.5));

        var finishedPacket = ServerPacketWriter.SendMessage("BasilBot", "Countdown finished",
            match.ChatChannelName, BotBootstrapService.BotId);
        Assert.Contains(finishedPacket, MultiplayerTestSupport.Chunk(host.Dequeue()));
    }

    [Theory]
    [InlineData(5, new[] { 4, 3, 2, 1 })]
    [InlineData(3, new[] { 2, 1 })]
    [InlineData(45, new[] { 30, 10, 5, 4, 3, 2, 1 })]
    // 60 is within the 5s near-total ignore window (61-60=1) — announcing it right after
    // "Queued...61 seconds" would be redundant, so it's dropped.
    [InlineData(61, new[] { 30, 10, 5, 4, 3, 2, 1 })]
    [InlineData(65, new[] { 30, 10, 5, 4, 3, 2, 1 })]
    // Long countdowns get an extra reminder every 60s on top of the fixed marks.
    [InlineData(300, new[] { 240, 180, 120, 60, 30, 10, 5, 4, 3, 2, 1 })]
    public void ComputeAnnounceCheckpoints_ReturnsExpectedMarks(int total, int[] expected)
    {
        var result = MpCommandService.ComputeAnnounceCheckpoints(total);

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

        var reply = await MakeService().HandleAsync(referee, match, "kick", ["host"]);

        Assert.Null(host.Match);
        Assert.Equal("Kicked host from the match", reply);
    }

    [Fact]
    public async Task HandleAsync_Kick_RefereeTarget_RemovesFromRoomButKeepsRefereeAuthority()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        var referee = MultiplayerTestSupport.MakePlayer(2, "referee");
        _fixture.RegisterAll(host, referee);
        var match = _fixture.CreateMatch(host);
        await _fixture.MatchMembership.Join(referee, match, "");
        match.AddReferee(referee.Id);

        var reply = await MakeService().HandleAsync(host, match, "kick", ["referee"]);

        Assert.Null(referee.Match);
        Assert.Contains(referee.Id, match.Referees);
        Assert.Equal("Kicked referee from the match", reply);
    }

    [Fact]
    public async Task HandleAsync_Kick_NonHostTarget_RemovesFromMatch()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        var other = MultiplayerTestSupport.MakePlayer(2, "other");
        _fixture.RegisterAll(host, other);
        var match = _fixture.CreateMatch(host);
        await _fixture.MatchMembership.Join(other, match, "");

        var reply = await MakeService().HandleAsync(host, match, "kick", ["other"]);

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
        await _fixture.MatchMembership.Join(other, match, "");

        var reply = await MakeService().HandleAsync(host, match, "ban", ["other"]);

        Assert.Null(other.Match);
        Assert.Contains(other.Id, match.BannedIds);
        Assert.Equal("Banned other from the match", reply);

        var rejoined = await _fixture.MatchMembership.Join(other, match, "");
        Assert.False(rejoined);
    }

    [Fact]
    public async Task HandleAsync_Ban_NotInMatch_Rejected()
    {
        // Ban only ever applies to physical room presence — an absent player can't be targeted.
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        var other = MultiplayerTestSupport.MakePlayer(2, "other");
        _fixture.RegisterAll(host, other);
        var match = _fixture.CreateMatch(host);

        var reply = await MakeService().HandleAsync(host, match, "ban", ["other"]);

        Assert.DoesNotContain(other.Id, match.BannedIds);
        Assert.Equal("other is not in this match.", reply);
    }

    [Fact]
    public async Task HandleAsync_Ban_RefereeTarget_RemovesFromRoomButKeepsRefereeAuthority()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        var referee = MultiplayerTestSupport.MakePlayer(2, "referee");
        _fixture.RegisterAll(host, referee);
        var match = _fixture.CreateMatch(host);
        await _fixture.MatchMembership.Join(referee, match, "");
        match.AddReferee(referee.Id);

        var reply = await MakeService().HandleAsync(host, match, "ban", ["referee"]);

        Assert.Contains(referee.Id, match.BannedIds);
        Assert.Contains(referee.Id, match.Referees);
        Assert.Null(referee.Match);
        Assert.Equal("Banned referee from the match", reply);
    }

    [Fact]
    public async Task HandleAsync_Unban_OfflinePlayer_RemovesFromBannedIds()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);
        match.AddBan(99);
        _users.FetchByNameAsync("offline_guy", Arg.Any<CancellationToken>()).Returns(MakeUser(99, "offline_guy"));

        var reply = await MakeService().HandleAsync(host, match, "unban", ["offline_guy"]);

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

        await MakeService().HandleAsync(host, match, "unban", ["other"]);
        var rejoined = await _fixture.MatchMembership.Join(other, match, "");

        Assert.True(rejoined);
    }

    [Fact]
    public async Task HandleAsync_Unban_NotBanned_ReturnsNotBannedMessage()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);
        _users.FetchByNameAsync("other", Arg.Any<CancellationToken>()).Returns(MakeUser(2, "other"));

        var reply = await MakeService().HandleAsync(host, match, "unban", ["other"]);

        Assert.Equal("other is not banned from this match.", reply);
    }

    [Fact]
    public async Task HandleAsync_Close_TearsDownRegardlessOfOccupancy()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        var other = MultiplayerTestSupport.MakePlayer(2, "other");
        _fixture.RegisterAll(host, other);
        var match = _fixture.CreateMatch(host);
        await _fixture.MatchMembership.Join(other, match, "");

        var reply = await MakeService().HandleAsync(host, match, "close", []);

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

        var reply = await MakeService().HandleAsync(host, match, "set", ["2", "1", "8"]);

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

        var reply = await MakeService().HandleAsync(host, match, "set", ["2"]);

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

        var reply = await MakeService().HandleAsync(host, match, "set", ["4", "0", "8"]);

        Assert.Equal("Usage: !mp set <teammode 0-3> [scoremode 0-3] [size 1-16]", reply);
    }

    [Fact]
    public async Task HandleAsync_Set_SizeOutOfRange_Clamps()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);

        var reply = await MakeService().HandleAsync(host, match, "set", ["0", "0", "99"]);

        Assert.Equal(SlotStatus.Open, match.Slots[15].Status);
        Assert.Equal("Changed match settings to HeadToHead, Score, 16 slots.", reply);
    }

    [Fact]
    public async Task MakeAsync_CreatesMatch_JoinsCreatorAsHostAndReferee()
    {
        var sender = MultiplayerTestSupport.MakePlayer(1, "creator");
        _fixture.RegisterAll(sender);

        var reply = await MakeService().MakeAsync(sender, ["My", "Tournament"]);

        Assert.Same(sender.Match, _fixture.MatchRegistry.All.Single());
        Assert.Equal(sender.Id, sender.Match!.HostId);
        Assert.Contains(sender.Id, sender.Match.Referees);
        Assert.True(sender.Match.CreatedViaMakeCommand);
        Assert.Equal("My Tournament", sender.Match.Name);
        Assert.Contains("Created the match", reply);
    }

    [Fact]
    public async Task MakeAsync_Makeprivate_BehavesIdenticallyToMake()
    {
        var makeSender = MultiplayerTestSupport.MakePlayer(1, "make_creator");
        var makePrivateSender = MultiplayerTestSupport.MakePlayer(2, "makeprivate_creator");
        _fixture.RegisterAll(makeSender, makePrivateSender);

        // `make` and `makeprivate` both route to MakeAsync with the exact same arguments — the
        // dispatcher-level alias is what's under test elsewhere; this asserts MakeAsync itself has no
        // hidden branch that would make the two behave differently.
        await MakeService().MakeAsync(makeSender, ["Room"]);
        await MakeService().MakeAsync(makePrivateSender, ["Room"]);

        var first = makeSender.Match!;
        var second = makePrivateSender.Match!;
        Assert.Equal(first.Name, second.Name);
        Assert.Equal(first.Password, second.Password);
    }

    [Fact]
    public async Task MakeAsync_AllPlayersLeave_DoesNotTearDownWhileRefereesRemain()
    {
        // Reversed from the room's normal all-slots-empty auto-teardown: a `!mp make` room persists
        // until `!mp close` or its referee list empties, regardless of player occupancy.
        var sender = MultiplayerTestSupport.MakePlayer(1, "creator");
        _fixture.RegisterAll(sender);
        await MakeService().MakeAsync(sender, ["Room"]);
        var match = sender.Match!;

        await _fixture.MatchMembership.Leave(sender, match);

        Assert.NotNull(_fixture.MatchRegistry.GetById(match.Id));
        Assert.Contains(sender.Id, match.Referees);
    }

    [Fact]
    public async Task RemoveRef_LastReferee_RejectedAndMatchStaysOpen()
    {
        var sender = MultiplayerTestSupport.MakePlayer(1, "creator");
        _fixture.RegisterAll(sender);
        var service = MakeService();
        await service.MakeAsync(sender, ["Room"]);
        var match = sender.Match!;

        var reply = await service.HandleAsync(sender, match, "removeref", ["creator"]);

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

        await service.HandleAsync(host, match, "removeref", ["referee"]);

        Assert.NotNull(_fixture.MatchRegistry.GetById(match.Id));
        Assert.DoesNotContain(referee.Id, match.Referees);
    }

    [Fact]
    public async Task MakeAsync_SetsSenderScopeToNewMatch()
    {
        var sender = MultiplayerTestSupport.MakePlayer(1, "creator");
        _fixture.RegisterAll(sender);

        var reply = await MakeService().MakeAsync(sender, ["Room"]);

        Assert.Equal(sender.Match!.DbId, sender.MpScopeMatchId);
        Assert.Contains("scoped to this match", reply);
    }

    [Fact]
    public void SetScopeAsync_NoArgs_NoScope_ReportsNotScoped()
    {
        var sender = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(sender);

        var reply = MakeService().SetScopeAsync(sender, []);

        Assert.Equal("You're not scoped to any match.", reply);
    }

    [Fact]
    public void SetScopeAsync_NoArgs_WithScope_ReportsCurrentMatch()
    {
        var sender = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(sender);
        var match = _fixture.CreateMatch(sender);
        sender.MpScopeMatchId = match.DbId;

        var reply = MakeService().SetScopeAsync(sender, []);

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

        var reply = MakeService().SetScopeAsync(referee, [match.DbId.ToString()]);

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

        var reply = MakeService().SetScopeAsync(other, [match.DbId.ToString()]);

        Assert.Null(other.MpScopeMatchId);
        Assert.Contains("not a referee", reply);
    }

    [Fact]
    public void SetScopeAsync_UnknownMatchId_Rejected()
    {
        var sender = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(sender);

        var reply = MakeService().SetScopeAsync(sender, ["999"]);

        Assert.Contains("No active match", reply);
    }

    [Fact]
    public async Task HandleAsync_Private_NonReferee_SilentlyIgnored()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        var other = MultiplayerTestSupport.MakePlayer(2, "other");
        _fixture.RegisterAll(host, other);
        var match = _fixture.CreateMatch(host);

        var reply = await MakeService().HandleAsync(other, match, "private", []);

        Assert.Null(reply);
    }

    [Fact]
    public async Task HandleAsync_Private_Referee_ShowsStatus()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);

        var reply = await MakeService().HandleAsync(host, match, "private", []);

        Assert.Contains("not private", reply);
    }

    [Fact]
    public async Task HandleAsync_Private_Referee_SetsTrue()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);

        var reply = await MakeService().HandleAsync(host, match, "private", ["1"]);

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

        var reply = await MakeService().HandleAsync(host, match, "private", ["0"]);

        Assert.False(match.IsPrivate);
        Assert.Contains("now public", reply);
    }

    [Fact]
    public async Task HandleAsync_Makeprivate_SetsPrivate()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);

        var reply = await MakeService().HandleAsync(host, match, "makeprivate", []);

        Assert.True(match.IsPrivate);
        Assert.Contains("now private", reply);
    }

    [Fact]
    public async Task JoinAsync_NonExistent_ReturnsError()
    {
        var sender = MultiplayerTestSupport.MakePlayer(1, "player");
        _fixture.RegisterAll(sender);

        var reply = await MakeService().JoinAsync(sender, ["999"]);

        Assert.Contains("No active match", reply);
    }

    [Fact]
    public async Task JoinAsync_Private_Fails()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        var player = MultiplayerTestSupport.MakePlayer(2, "player");
        _fixture.RegisterAll(host, player);
        var match = _fixture.CreateMatch(host);
        match.IsPrivate = true;

        var reply = await MakeService().JoinAsync(player, ["0"]);

        Assert.Contains("private", reply);
    }

    [Fact]
    public async Task JoinAsync_Normal_Succeeds()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        var player = MultiplayerTestSupport.MakePlayer(2, "player");
        _fixture.RegisterAll(host, player);
        var match = _fixture.CreateMatch(host);

        var reply = await MakeService().JoinAsync(player, ["0"]);

        Assert.NotNull(reply);
        Assert.Contains("Joined match", reply);
    }

    private static User MakeUser(int id, string name)
    {
        return new User(id, name, Country.Xx, UserPrivileges.Unrestricted, default);
    }
}