using Bancho.Application.Sessions;
using Bancho.Application.UseCases.Multiplayer;
using Bancho.Domain;

namespace Bancho.Application.Commands;

/// <summary>Ported from app/commands.py's mp_force — overrides silences/password, staff-only.</summary>
public sealed class MpForceCommand(IPlayerSessionRegistry sessionRegistry, MatchMembershipService matchMembership) : IMpSubCommand
{
    public string Trigger => "force";

    public IReadOnlyList<string> Aliases => ["f"];

    public Privileges RequiredPriv => Privileges.Administrator;

    public bool Hidden => true;

    public string? Description => "Force a player into the current match by name.";

    public async Task<string?> HandleAsync(MpCommandContext ctx)
    {
        if (ctx.Args.Count != 1)
        {
            return "Invalid syntax: !mp force <name>";
        }

        var target = sessionRegistry.GetByName(ctx.Args[0]);
        if (target is null)
        {
            return "Could not find a user by that name.";
        }

        var match = ctx.Match;
        await match.Lock.WaitAsync();
        try
        {
            matchMembership.Join(target, match, match.Password);
        }
        finally
        {
            match.Lock.Release();
        }

        return "Welcome.";
    }
}
