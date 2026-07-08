using NSubstitute;
using OpenOsuTournament.Bancho.Application.Abstractions;
using OpenOsuTournament.Bancho.Application.Abstractions.Beatmaps;
using OpenOsuTournament.Bancho.Application.Sessions.Multiplayer;
using OpenOsuTournament.Bancho.Application.Tests.PacketHandlers;
using OpenOsuTournament.Bancho.Application.UseCases.Bot;
using OpenOsuTournament.Bancho.Domain;
using OpenOsuTournament.Bancho.Domain.Beatmaps;
using OpenOsuTournament.Bancho.Domain.Multiplayer;

namespace OpenOsuTournament.Bancho.Application.Tests.UseCases.Bot;

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

    private MpCommandService MakeService()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        return new MpCommandService(_fixture.MatchMembership, _fixture.MatchPersistence, _maps,
            _fixture.SessionRegistry, clock);
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
    public async Task HandleAsync_LockEmptySlot_MarksLocked()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);

        var reply = await MakeService().HandleAsync(host, match, "lock", ["2"]);

        Assert.Equal(SlotStatus.Locked, match.Slots[1].Status);
        Assert.Equal("Slot 2 locked.", reply);
    }

    [Fact]
    public async Task HandleAsync_LockOccupiedSlot_Rejected()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);

        var reply = await MakeService().HandleAsync(host, match, "lock", ["1"]);

        Assert.Equal("Can't lock an occupied slot.", reply);
        Assert.Equal(SlotStatus.NotReady, match.Slots[0].Status);
    }

    [Fact]
    public async Task HandleAsync_Unlock_ReopensLockedSlot()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);
        match.Slots[1].Status = SlotStatus.Locked;

        var reply = await MakeService().HandleAsync(host, match, "unlock", ["2"]);

        Assert.Equal(SlotStatus.Open, match.Slots[1].Status);
        Assert.Equal("Slot 2 unlocked.", reply);
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
        Assert.Equal("Moved other to slot 5.", reply);
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
        Assert.Equal("other is now the host.", reply);
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

    [Theory]
    [InlineData(new[] { "off" }, "")]
    [InlineData(new string[] { }, "")]
    public async Task HandleAsync_PasswordOff_ClearsPassword(string[] args, string expected)
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);
        match.Password = "secret";

        await MakeService().HandleAsync(host, match, "password", args);

        Assert.Equal(expected, match.Password);
    }

    [Fact]
    public async Task HandleAsync_PasswordRandpw_SetsNonEmptyPassword()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);
        match.Password = "";

        await MakeService().HandleAsync(host, match, "password", ["randpw"]);

        Assert.NotEmpty(match.Password);
    }

    [Fact]
    public async Task HandleAsync_AddRef_NonHostReferee_Denied()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        var referee = MultiplayerTestSupport.MakePlayer(2, "referee");
        var other = MultiplayerTestSupport.MakePlayer(3, "other");
        _fixture.RegisterAll(host, referee, other);
        var match = _fixture.CreateMatch(host);
        match.AddReferee(referee.Id);

        var reply = await MakeService().HandleAsync(referee, match, "addref", ["other"]);

        Assert.Null(reply);
        Assert.DoesNotContain(other.Id, match.Referees);
    }

    [Fact]
    public async Task HandleAsync_AddRefThenListRef_HostOnlyAndReflectsAddition()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        var other = MultiplayerTestSupport.MakePlayer(2, "other");
        _fixture.RegisterAll(host, other);
        var match = _fixture.CreateMatch(host);
        var service = MakeService();

        await service.HandleAsync(host, match, "addref", ["other"]);
        var listing = await service.HandleAsync(host, match, "listref", []);

        Assert.Contains(other.Id, match.Referees);
        Assert.Contains("other", listing);
    }

    [Fact]
    public async Task HandleAsync_RemoveRef_HostOnly_RemovesReferee()
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
    public async Task HandleAsync_Team_SetsTargetSlotTeam()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);

        await MakeService().HandleAsync(host, match, "team", ["host", "blue"]);

        Assert.Equal(MatchTeams.Blue, match.Slots[0].Team);
    }

    [Fact]
    public async Task HandleAsync_Teams_ChangesTeamTypeAndReassignsTeams()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);

        await MakeService().HandleAsync(host, match, "teams", ["teamvs"]);

        Assert.Equal(MatchTeamTypes.TeamVs, match.TeamType);
        Assert.Equal(MatchTeams.Red, match.Slots[0].Team);
    }

    [Fact]
    public async Task HandleAsync_Condition_SetsWinCondition()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);

        await MakeService().HandleAsync(host, match, "condition", ["combo"]);

        Assert.Equal(MatchWinConditions.Combo, match.WinCondition);
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
    public async Task HandleAsync_Mods_WhileFreemod_Rejected()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);
        match.Freemods = true;

        var reply = await MakeService().HandleAsync(host, match, "mods", ["HD"]);

        Assert.Equal("Match is in freemod — per-player mods can't be set with !mp mods.", reply);
    }

    [Fact]
    public async Task HandleAsync_FreemodsOn_SplitsHostModsToSlot()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);
        match.Mods = Mods.Hidden;

        await MakeService().HandleAsync(host, match, "freemods", ["on"]);

        Assert.True(match.Freemods);
        Assert.Equal(Mods.Hidden, match.Slots[0].Mods);
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
        Assert.Equal("Match started.", reply);
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
        Assert.Equal("Match aborted.", reply);
    }

    [Fact]
    public async Task HandleAsync_Kick_Host_Rejected()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        var referee = MultiplayerTestSupport.MakePlayer(2, "referee");
        _fixture.RegisterAll(host, referee);
        var match = _fixture.CreateMatch(host);
        match.AddReferee(referee.Id);

        var reply = await MakeService().HandleAsync(referee, match, "kick", ["host"]);

        Assert.Equal("Can't kick the host.", reply);
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
        Assert.Equal("Kicked other.", reply);
    }
}
