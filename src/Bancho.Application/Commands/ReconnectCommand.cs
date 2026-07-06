using Bancho.Application.Sessions;
using Bancho.Domain;

namespace Bancho.Application.Commands;

/// <summary>Ported from app/commands.py's reconnect.</summary>
public sealed class ReconnectCommand(IPlayerSessionRegistry sessionRegistry, PlayerLogoutService logoutService) : ICommand
{
    public string Trigger => "reconnect";

    public IReadOnlyList<string> Aliases => [];

    public Privileges RequiredPriv => Privileges.Unrestricted;

    public bool Hidden => false;

    public string Description => "Disconnect and reconnect a given player (or self) to the server.";

    public Task<string?> HandleAsync(CommandContext ctx)
    {
        PlayerSession target;

        if (ctx.Args.Count > 0)
        {
            if ((ctx.Player.Priv & Privileges.Administrator) == 0)
            {
                return Task.FromResult<string?>(null);
            }

            var found = sessionRegistry.GetByName(string.Join(' ', ctx.Args));
            if (found is null)
            {
                return Task.FromResult<string?>("Player not found");
            }

            target = found;
        }
        else
        {
            target = ctx.Player;
        }

        logoutService.Logout(target);

        return Task.FromResult<string?>(null);
    }
}
