using Bancho.Application.UseCases.Multiplayer;
using Bancho.Domain;

namespace Bancho.Application.Commands;

/// <summary>Ported from app/commands.py's mp_unlock.</summary>
public sealed class MpUnlockCommand(MatchMembershipService matchMembership) : IMpSubCommand
{
    public string Trigger => "unlock";

    public IReadOnlyList<string> Aliases => [];

    public Privileges RequiredPriv => Privileges.Unrestricted;

    public bool Hidden => false;

    public string? Description => "Unlock locked slots in the current match.";

    public async Task<string?> HandleAsync(MpCommandContext ctx)
    {
        var match = ctx.Match;
        await match.Lock.WaitAsync();
        try
        {
            foreach (var slot in match.Slots)
            {
                if (slot.Status == SlotStatus.Locked)
                {
                    slot.Status = SlotStatus.Open;
                }
            }

            matchMembership.EnqueueState(match);
        }
        finally
        {
            match.Lock.Release();
        }

        return "All locked slots unlocked.";
    }
}
