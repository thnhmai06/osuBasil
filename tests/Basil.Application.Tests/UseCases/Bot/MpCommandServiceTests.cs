using Basil.Application.Abstractions;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Users;
using Basil.Application.Tests.PacketHandlers;
using Basil.Application.UseCases.Bot;
using Basil.Domain;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
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
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        return new MpCommandService(_fixture.MatchMembership, _fixture.MatchRegistry, _fixture.MatchPersistence, _maps,
            _fixture.SessionRegistry, _users, clock);
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
        Assert.False(_fixture.MatchMembership.Join(other, match, ""));
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
        Assert.True(_fixture.MatchMembership.Join(other, match, ""));
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
        _fixture.MatchMembership.Join(other, match, "");

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
        _fixture.MatchMembership.Join(other, match, "");

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
        _fixture.MatchMembership.Join(other, match, "");

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

        Assert.Equal(MatchTeams.Blue, match.Slots[0].Team);
    }

    [Fact]
    public async Task HandleAsync_Map_KnownBeatmap_UpdatesMatchMap()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);
        var bmap = new Beatmap(new string('a', 32), 500, 1, "Artist", "Title", "Version", "creator", DateTime.UtcNow,
            120, 500, RankedStatus.Ranked, false, 0, 0, GameMode.VanillaOsu, 180, 4, 8, 9, 5, 6.5, "file.osu");
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
        Assert.True(cts!.IsCancellationRequested);
        Assert.Equal("Countdown aborted", reply);
    }

    [Theory]
    [InlineData(5, new[] { 4, 3, 2, 1 })]
    [InlineData(3, new[] { 2, 1 })]
    [InlineData(45, new[] { 30, 10, 5, 4, 3, 2, 1 })]
    [InlineData(61, new[] { 60, 30, 10, 5, 4, 3, 2, 1 })]
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
        _fixture.MatchMembership.Join(referee, match, "");
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
        _fixture.MatchMembership.Join(other, match, "");

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
        _fixture.MatchMembership.Join(other, match, "");

        var reply = await MakeService().HandleAsync(host, match, "ban", ["other"]);

        Assert.Null(other.Match);
        Assert.Contains(other.Id, match.BannedIds);
        Assert.Equal("Banned other from the match", reply);

        var rejoined = _fixture.MatchMembership.Join(other, match, "");
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
        _fixture.MatchMembership.Join(referee, match, "");
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
        var rejoined = _fixture.MatchMembership.Join(other, match, "");

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
        _fixture.MatchMembership.Join(other, match, "");

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

        Assert.Equal(MatchTeamTypes.TeamVs, match.TeamType);
        Assert.Equal(MatchWinConditions.Accuracy, match.WinCondition);
        Assert.Equal(SlotStatus.Locked, match.Slots[8].Status);
        Assert.Equal("Changed match settings to TeamVs, Accuracy, 8 slots.", reply);
    }

    [Fact]
    public async Task HandleAsync_Set_TeammodeOnly_LeavesScoremodeAndSizeUnchanged()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);
        match.WinCondition = MatchWinConditions.Combo;

        var reply = await MakeService().HandleAsync(host, match, "set", ["2"]);

        Assert.Equal(MatchTeamTypes.TeamVs, match.TeamType);
        Assert.Equal(MatchWinConditions.Combo, match.WinCondition);
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

        _fixture.MatchMembership.Leave(sender, match);

        Assert.NotNull(_fixture.MatchRegistry.GetById(match.Id));
        Assert.Contains(sender.Id, match.Referees);
    }

    [Fact]
    public async Task RemoveRef_LastRefereeOnMakeRoom_ClosesMatch()
    {
        var sender = MultiplayerTestSupport.MakePlayer(1, "creator");
        _fixture.RegisterAll(sender);
        var service = MakeService();
        await service.MakeAsync(sender, ["Room"]);
        var match = sender.Match!;

        var reply = await service.HandleAsync(sender, match, "removeref", ["creator"]);

        Assert.Null(_fixture.MatchRegistry.GetById(match.Id));
        Assert.Contains("match closed", reply);
    }

    [Fact]
    public async Task RemoveRef_LastRefereeOnNormalRoom_DoesNotClose()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        var referee = MultiplayerTestSupport.MakePlayer(2, "referee");
        _fixture.RegisterAll(host, referee);
        var match = _fixture.CreateMatch(host, hostIsReferee: false);
        match.AddReferee(referee.Id);
        var service = MakeService();

        await service.HandleAsync(referee, match, "removeref", ["referee"]);

        Assert.NotNull(_fixture.MatchRegistry.GetById(match.Id));
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

    private static User MakeUser(int id, string name)
    {
        return new User(id, name, name.ToLowerInvariant(), null, 1, "xx", 0, 0, 0, 0, 0, 0, 0, 0, null, null, null);
    }
}