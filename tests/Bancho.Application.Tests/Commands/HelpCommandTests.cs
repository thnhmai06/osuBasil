using Bancho.Application.Commands;
using Bancho.Application.Configuration;
using Bancho.Application.Sessions;
using Bancho.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bancho.Application.Tests.Commands;

/// <summary>
/// Ported from app/commands.py's _help — lists documented commands the player has priv for.
/// The "Command sets" section (!mp/!pool/!clan) is dropped since those don't exist yet.
/// </summary>
public class HelpCommandTests
{
    private sealed class FakeCommand(string trigger, Privileges requiredPriv, string? description) : ICommand
    {
        public string Trigger => trigger;
        public IReadOnlyList<string> Aliases => [];
        public Privileges RequiredPriv => requiredPriv;
        public bool Hidden => false;
        public string? Description => description;
        public Task<string?> HandleAsync(CommandContext ctx) => Task.FromResult<string?>(null);
    }

    private static readonly IOptions<ServerBehaviorOptions> ServerOptions = Options.Create(new ServerBehaviorOptions
    {
        Domain = "test.local", CommandPrefix = "!", MenuIconUrl = "https://x", MenuOnclickUrl = "https://x",
    });

    private static CommandContext MakeContext(PlayerSession player) => new(player, [], null, null);

    [Fact]
    public async Task HandleAsync_ListsDocumentedCommandsPlayerHasPrivilegeFor()
    {
        var serviceProvider = new ServiceCollection()
            .AddSingleton<ICommand>(new FakeCommand("roll", Privileges.Unrestricted, "Roll a die."))
            .AddSingleton<ICommand>(new FakeCommand("silence", Privileges.Moderator, "Silence a user."))
            .AddSingleton<ICommand>(new FakeCommand("secret", Privileges.Unrestricted, null))
            .BuildServiceProvider();
        var player = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);

        var response = await new HelpCommand(serviceProvider, ServerOptions).HandleAsync(MakeContext(player));

        Assert.Contains("!roll: Roll a die.", response);
        Assert.DoesNotContain("silence", response);
        Assert.DoesNotContain("secret", response);
    }
}
