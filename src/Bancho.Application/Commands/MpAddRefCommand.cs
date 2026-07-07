using Bancho.Application.Sessions;
using Bancho.Domain;

namespace Bancho.Application.Commands;

/// <summary>Ported from app/commands.py's mp_addref.</summary>
public sealed class MpAddRefCommand(IPlayerSessionRegistry sessionRegistry) : IMpSubCommand
{
    public string Trigger => "addref";

    public IReadOnlyList<string> Aliases => [];

    public Privileges RequiredPriv => Privileges.Unrestricted;

    public bool Hidden => false;

    public string? Description => "Add a referee to the current match by name.";

    public async Task<string?> HandleAsync(MpCommandContext ctx)
    {
        if (ctx.Args.Count != 1)
        {
            return "Invalid syntax: !mp addref <name>";
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
            if (match.GetSlot(target.Id) is null)
            {
                return "User must be in the current match!";
            }

            if (match.IsReferee(target.Id))
            {
                return $"{target.Name} is already a match referee!";
            }

            match.AddReferee(target.Id);
        }
        finally
        {
            match.Lock.Release();
        }

        return $"{target.Name} added to match referees.";
    }
}
