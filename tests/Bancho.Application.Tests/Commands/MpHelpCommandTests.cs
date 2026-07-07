using Bancho.Application.Commands;
using Bancho.Application.Configuration;
using Bancho.Application.Sessions;
using Bancho.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bancho.Application.Tests.Commands;

/// <summary>Ported from app/commands.py's mp_help — lists mp subcommands the player has priv for.</summary>
public class MpHelpCommandTests
{
    private sealed class FakeSubCommand(string trigger, Privileges requiredPriv, string? description) : IMpSubCommand
    {
        public string Trigger => trigger;
        public IReadOnlyList<string> Aliases => [];
        public Privileges RequiredPriv => requiredPriv;
        public bool Hidden => false;
        public string? Description => description;
        public Task<string?> HandleAsync(MpCommandContext ctx) => Task.FromResult<string?>(null);
    }

    private static readonly IOptions<ServerBehaviorOptions> ServerOptions = Options.Create(new ServerBehaviorOptions
    {
        Domain = "test.local", CommandPrefix = "!", MenuIconUrl = "https://x", MenuOnclickUrl = "https://x",
    });

    private static MatchSession MakeMatch(int hostId) => new(
        id: 0, name: "test match", password: "pw", hasPublicHistory: true,
        mapName: "Some Map", mapId: 100, mapMd5: new string('a', 32), hostId: hostId,
        mode: GameMode.VanillaOsu, mods: Mods.NoMod, winCondition: MatchWinConditions.Score,
        teamType: MatchTeamTypes.HeadToHead, freemods: false, seed: 0, chatChannelName: "#multi_0");

    [Fact]
    public async Task HandleAsync_ListsDocumentedSubCommandsPlayerHasPrivilegeFor()
    {
        var serviceProvider = new ServiceCollection()
            .AddSingleton<IMpSubCommand>(new FakeSubCommand("start", Privileges.Unrestricted, "Start the match."))
            .AddSingleton<IMpSubCommand>(new FakeSubCommand("force", Privileges.Administrator, "Force a player in."))
            .AddSingleton<IMpSubCommand>(new FakeSubCommand("secret", Privileges.Unrestricted, null))
            .BuildServiceProvider();
        var player = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var ctx = new MpCommandContext(player, [], MakeMatch(hostId: 1));

        var response = await new MpHelpCommand(serviceProvider, ServerOptions).HandleAsync(ctx);

        Assert.Contains("!mp start: Start the match.", response);
        Assert.DoesNotContain("force", response);
        Assert.DoesNotContain("secret", response);
    }
}
