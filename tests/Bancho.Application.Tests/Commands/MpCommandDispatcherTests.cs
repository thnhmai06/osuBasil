using Bancho.Application.Commands;
using Bancho.Application.Sessions;
using Bancho.Domain;

namespace Bancho.Application.Tests.Commands;

/// <summary>
/// Ported from app/commands.py's process_commands' CommandSet-matching branch (trigger "mp") and
/// the ensure_match decorator: must be in a match, sent in that match's own chat channel, and be a
/// referee/host or tourney manager — except "help", which only needs the first two.
/// </summary>
public class MpCommandDispatcherTests
{
    private sealed class FakeSubCommand(string trigger, Privileges requiredPriv, Func<MpCommandContext, Task<string?>>? onHandle = null) : IMpSubCommand
    {
        public string Trigger => trigger;
        public IReadOnlyList<string> Aliases => [];
        public Privileges RequiredPriv => requiredPriv;
        public bool Hidden => false;
        public string? Description => null;
        public Task<string?> HandleAsync(MpCommandContext ctx) => (onHandle ?? (_ => Task.FromResult<string?>("ok")))(ctx);
    }

    private static PlayerSession MakePlayer(int id, Privileges priv = Privileges.Unrestricted) => new(id, $"p{id}", "token", priv, 0.0);

    private static MatchSession MakeMatch(int hostId, string chatChannelName = "#multi_0") => new(
        id: 0, name: "test match", password: "pw", hasPublicHistory: true,
        mapName: "Some Map", mapId: 100, mapMd5: new string('a', 32), hostId: hostId,
        mode: GameMode.VanillaOsu, mods: Mods.NoMod, winCondition: MatchWinConditions.Score,
        teamType: MatchTeamTypes.HeadToHead, freemods: false, seed: 0, chatChannelName: chatChannelName);

    private static ChannelSession MakeChannel(string name) => new(id: 1, name: name, topic: "", readPriv: 0, writePriv: 0, autoJoin: false);

    [Fact]
    public async Task HandleAsync_NoArgs_DefaultsToHelp()
    {
        var help = new FakeSubCommand("help", Privileges.Unrestricted);
        var dispatcher = new MpCommandDispatcher([help]);
        var host = MakePlayer(1);
        var match = MakeMatch(hostId: 1);
        host.Match = match;

        var response = await dispatcher.HandleAsync(new CommandContext(host, [], MakeChannel(match.ChatChannelName), null));

        Assert.Equal("ok", response);
    }

    [Fact]
    public async Task HandleAsync_PlayerNotInAMatch_ReturnsNullSilently()
    {
        var dispatcher = new MpCommandDispatcher([new FakeSubCommand("start", Privileges.Unrestricted)]);
        var player = MakePlayer(1);

        var response = await dispatcher.HandleAsync(new CommandContext(player, ["start"], MakeChannel("#multi_0"), null));

        Assert.Null(response);
    }

    [Fact]
    public async Task HandleAsync_SentOutsideTheMatchsOwnChannel_ReturnsNullSilently()
    {
        var dispatcher = new MpCommandDispatcher([new FakeSubCommand("start", Privileges.Unrestricted)]);
        var host = MakePlayer(1);
        var match = MakeMatch(hostId: 1);
        host.Match = match;

        var response = await dispatcher.HandleAsync(new CommandContext(host, ["start"], MakeChannel("#osu"), null));

        Assert.Null(response);
    }

    [Fact]
    public async Task HandleAsync_NonRefereeNonTourneyManager_ReturnsNullSilently()
    {
        var dispatcher = new MpCommandDispatcher([new FakeSubCommand("start", Privileges.Unrestricted)]);
        var guest = MakePlayer(2);
        var match = MakeMatch(hostId: 1);
        guest.Match = match;

        var response = await dispatcher.HandleAsync(new CommandContext(guest, ["start"], MakeChannel(match.ChatChannelName), null));

        Assert.Null(response);
    }

    [Fact]
    public async Task HandleAsync_Host_IsAlwaysARefereeAndCanRunSubcommands()
    {
        var dispatcher = new MpCommandDispatcher([new FakeSubCommand("start", Privileges.Unrestricted)]);
        var host = MakePlayer(1);
        var match = MakeMatch(hostId: 1);
        host.Match = match;

        var response = await dispatcher.HandleAsync(new CommandContext(host, ["start"], MakeChannel(match.ChatChannelName), null));

        Assert.Equal("ok", response);
    }

    [Fact]
    public async Task HandleAsync_TourneyManager_BypassesTheRefereeCheck()
    {
        var dispatcher = new MpCommandDispatcher([new FakeSubCommand("start", Privileges.Unrestricted)]);
        var manager = MakePlayer(2, Privileges.Unrestricted | Privileges.TourneyManager);
        var match = MakeMatch(hostId: 1);
        manager.Match = match;

        var response = await dispatcher.HandleAsync(new CommandContext(manager, ["start"], MakeChannel(match.ChatChannelName), null));

        Assert.Equal("ok", response);
    }

    [Fact]
    public async Task HandleAsync_Help_BypassesTheRefereeCheckEvenForANonReferee()
    {
        var dispatcher = new MpCommandDispatcher([new FakeSubCommand("help", Privileges.Unrestricted)]);
        var guest = MakePlayer(2);
        var match = MakeMatch(hostId: 1);
        guest.Match = match;

        var response = await dispatcher.HandleAsync(new CommandContext(guest, ["help"], MakeChannel(match.ChatChannelName), null));

        Assert.Equal("ok", response);
    }

    [Fact]
    public async Task HandleAsync_UnknownSubcommand_ReturnsNullSilently()
    {
        var dispatcher = new MpCommandDispatcher([new FakeSubCommand("start", Privileges.Unrestricted)]);
        var host = MakePlayer(1);
        var match = MakeMatch(hostId: 1);
        host.Match = match;

        var response = await dispatcher.HandleAsync(new CommandContext(host, ["nonexistent"], MakeChannel(match.ChatChannelName), null));

        Assert.Null(response);
    }

    [Fact]
    public async Task HandleAsync_InsufficientPrivilegeForTheSubcommand_ReturnsNullSilently()
    {
        var dispatcher = new MpCommandDispatcher([new FakeSubCommand("force", Privileges.Administrator)]);
        var host = MakePlayer(1);
        var match = MakeMatch(hostId: 1);
        host.Match = match;

        var response = await dispatcher.HandleAsync(new CommandContext(host, ["force"], MakeChannel(match.ChatChannelName), null));

        Assert.Null(response);
    }

    [Fact]
    public async Task HandleAsync_SubcommandReceivesArgsWithTheTriggerShiftedOff()
    {
        MpCommandContext? captured = null;
        var subCommand = new FakeSubCommand("host", Privileges.Unrestricted, ctx =>
        {
            captured = ctx;
            return Task.FromResult<string?>("ok");
        });
        var dispatcher = new MpCommandDispatcher([subCommand]);
        var hostPlayer = MakePlayer(1);
        var match = MakeMatch(hostId: 1);
        hostPlayer.Match = match;

        await dispatcher.HandleAsync(new CommandContext(hostPlayer, ["host", "cmyui"], MakeChannel(match.ChatChannelName), null));

        Assert.NotNull(captured);
        Assert.Equal(["cmyui"], captured!.Args);
        Assert.Same(match, captured.Match);
    }
}
