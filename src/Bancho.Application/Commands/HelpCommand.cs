using Bancho.Application.Configuration;
using Bancho.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bancho.Application.Commands;

/// <summary>
/// Ported from app/commands.py's _help. Python lists command sets (!mp/!pool/!clan) in a
/// separate "Command sets" section using each CommandSet's own doc string; here MpCommandDispatcher
/// is simply registered as a regular ICommand (Trigger "mp", Description "Multiplayer commands."),
/// so it already appears in the flat loop below without a second section — !pool/!clan will do the
/// same once those exist. Resolves IEnumerable&lt;ICommand&gt; lazily via IServiceProvider (instead
/// of constructor injection) since HelpCommand is itself one of the registered ICommands —
/// constructor-injecting the full list would be a circular dependency the DI container rejects at startup.
/// </summary>
public sealed class HelpCommand(IServiceProvider serviceProvider, IOptions<ServerBehaviorOptions> serverOptions) : ICommand
{
    public string Trigger => "help";

    public IReadOnlyList<string> Aliases => ["h"];

    public Privileges RequiredPriv => Privileges.Unrestricted;

    public bool Hidden => true;

    public string Description => "Show all documented commands the player can access.";

    public Task<string?> HandleAsync(CommandContext ctx)
    {
        var prefix = serverOptions.Value.CommandPrefix;
        var lines = new List<string> { "Individual commands", "-----------" };

        foreach (var command in serviceProvider.GetServices<ICommand>())
        {
            if (command.Description is null || (ctx.Player.Priv & command.RequiredPriv) != command.RequiredPriv)
            {
                continue;
            }

            lines.Add($"{prefix}{command.Trigger}: {command.Description}");
        }

        return Task.FromResult<string?>(string.Join('\n', lines));
    }
}
