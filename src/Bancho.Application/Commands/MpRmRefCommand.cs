using Bancho.Application.Sessions;
using Bancho.Domain;

namespace Bancho.Application.Commands;

/// <summary>Ported from app/commands.py's mp_rmref.</summary>
public sealed class MpRmRefCommand(IPlayerSessionRegistry sessionRegistry) : IMpSubCommand
{
    public string Trigger => "rmref";

    public IReadOnlyList<string> Aliases => [];

    public Privileges RequiredPriv => Privileges.Unrestricted;

    public bool Hidden => false;

    public string? Description => "Remove a referee from the current match by name.";

    public async Task<string?> HandleAsync(MpCommandContext ctx)
    {
        if (ctx.Args.Count != 1)
        {
            return "Invalid syntax: !mp rmref <name>";
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
            if (!match.IsReferee(target.Id))
            {
                return $"{target.Name} is not a match referee!";
            }

            if (target.Id == match.HostId)
            {
                return "The host is always a referee!";
            }

            match.RemoveReferee(target.Id);
        }
        finally
        {
            match.Lock.Release();
        }

        return $"{target.Name} removed from match referees.";
    }
}
