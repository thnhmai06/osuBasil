using Bancho.Application.Configuration;
using Bancho.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bancho.Application.Commands;

/// <summary>
/// Ported from app/commands.py's mp_help. Resolves IEnumerable&lt;IMpSubCommand&gt; lazily via
/// IServiceProvider (instead of constructor injection), since this command is itself one of the
/// registered IMpSubCommands — same circular-dependency workaround as HelpCommand.
/// </summary>
public sealed class MpHelpCommand(IServiceProvider serviceProvider, IOptions<ServerBehaviorOptions> serverOptions) : IMpSubCommand
{
    public string Trigger => "help";

    public IReadOnlyList<string> Aliases => ["h"];

    public Privileges RequiredPriv => Privileges.Unrestricted;

    public bool Hidden => false;

    public string? Description => "Show all documented multiplayer commands the player can access.";

    public Task<string?> HandleAsync(MpCommandContext ctx)
    {
        var prefix = serverOptions.Value.CommandPrefix;
        var lines = new List<string>();

        foreach (var subCommand in serviceProvider.GetServices<IMpSubCommand>())
        {
            if (subCommand.Description is null || (ctx.Player.Priv & subCommand.RequiredPriv) != subCommand.RequiredPriv)
            {
                continue;
            }

            lines.Add($"{prefix}mp {subCommand.Trigger}: {subCommand.Description}");
        }

        return Task.FromResult<string?>(string.Join('\n', lines));
    }
}
