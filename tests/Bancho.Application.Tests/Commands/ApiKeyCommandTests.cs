using Bancho.Application.Abstractions;
using Bancho.Application.Commands;
using Bancho.Application.Sessions;
using Bancho.Domain;
using NSubstitute;

namespace Bancho.Application.Tests.Commands;

/// <summary>Ported from app/commands.py's apikey.</summary>
public class ApiKeyCommandTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();

    private ApiKeyCommand MakeCommand() => new(_users);

    [Fact]
    public async Task HandleAsync_NotInDmWithBot_ReturnsDmOnlyError()
    {
        var player = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var channel = new ChannelSession(1, "#osu", "General", 0, 0, autoJoin: true);
        var ctx = new CommandContext(player, [], channel, null);

        var response = await MakeCommand().HandleAsync(ctx);

        Assert.Equal("Command only available in DMs with BanchoBot.", response);
        await _users.DidNotReceiveWithAnyArgs().UpdateApiKeyAsync(default, default!);
    }

    [Fact]
    public async Task HandleAsync_InDmWithBot_GeneratesAndPersistsNewApiKey()
    {
        var player = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var bot = new PlayerSession(2, "BanchoBot", "bot-token", Privileges.Unrestricted, 0.0, isBotClient: true);
        var ctx = new CommandContext(player, [], null, bot);

        var response = await MakeCommand().HandleAsync(ctx);

        await _users.Received(1).UpdateApiKeyAsync(1, Arg.Any<string>());
        Assert.StartsWith("API key generated.", response);
    }
}
