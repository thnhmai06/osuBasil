using Bancho.Application.UseCases.Multiplayer;
using Bancho.Domain;
using Bancho.Protocol;

namespace Bancho.Application.Commands;

/// <summary>Ported from app/commands.py's mp_abort.</summary>
public sealed class MpAbortCommand(MatchMembershipService matchMembership) : IMpSubCommand
{
    public string Trigger => "abort";

    public IReadOnlyList<string> Aliases => ["a"];

    public Privileges RequiredPriv => Privileges.Unrestricted;

    public bool Hidden => false;

    public string? Description => "Abort the current in-progress multiplayer match.";

    public async Task<string?> HandleAsync(MpCommandContext ctx)
    {
        var match = ctx.Match;
        await match.Lock.WaitAsync();
        try
        {
            if (!match.InProgress)
            {
                return "Abort what?";
            }

            match.UnreadyPlayers(SlotStatus.Playing);
            match.ResetPlayersLoadedStatus();
            match.InProgress = false;
            matchMembership.Enqueue(match, ServerPacketWriter.MatchAbort());
            matchMembership.EnqueueState(match);
        }
        finally
        {
            match.Lock.Release();
        }

        return "Match aborted.";
    }
}
