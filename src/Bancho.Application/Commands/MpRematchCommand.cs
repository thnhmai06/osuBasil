using Bancho.Application.Sessions;
using Bancho.Domain;

namespace Bancho.Application.Commands;

/// <summary>Ported from app/commands.py's mp_rematch — restart a finished scrim, or roll back the last awarded point.</summary>
public sealed class MpRematchCommand(IPlayerSessionRegistry sessionRegistry) : IMpSubCommand
{
    public string Trigger => "rematch";

    public IReadOnlyList<string> Aliases => ["rm"];

    public Privileges RequiredPriv => Privileges.Unrestricted;

    public bool Hidden => false;

    public string? Description => "Restart a scrim, or roll back previous match point.";

    public async Task<string?> HandleAsync(MpCommandContext ctx)
    {
        if (ctx.Args.Count > 0)
        {
            return "Invalid syntax: !mp rematch";
        }

        if (ctx.Player.Id != ctx.Match.HostId)
        {
            return "Only available to the host.";
        }

        var match = ctx.Match;
        await match.Lock.WaitAsync();
        try
        {
            if (!match.IsScrimming)
            {
                if (match.WinningPoints == 0)
                {
                    return "No scrim to rematch; to start one, use !mp scrim.";
                }

                match.IsScrimming = true;
                return $"A rematch has been started by {ctx.Player.Name}; first to {match.WinningPoints} points wins. Best of luck!";
            }

            if (match.Winners.Count == 0)
            {
                return "No match points have yet been awarded!";
            }

            var recentWinner = match.Winners[^1];
            if (recentWinner is null)
            {
                return "The last point was a tie!";
            }

            match.DecrementMatchPoint(recentWinner.Value);
            match.PopLastWinner();

            return $"A point has been deducted from {DescribeParticipant(recentWinner.Value)}.";
        }
        finally
        {
            match.Lock.Release();
        }
    }

    private string DescribeParticipant(ScrimParticipant participant) => participant.PlayerId is { } playerId
        ? sessionRegistry.GetById(playerId)?.Name ?? $"player #{playerId}"
        : participant.Team!.Value.ToString();
}
