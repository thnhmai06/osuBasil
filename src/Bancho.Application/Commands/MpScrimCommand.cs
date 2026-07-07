using System.Text.RegularExpressions;
using Bancho.Domain;

namespace Bancho.Application.Commands;

/// <summary>
/// Ported from app/commands.py's mp_scrim (regexes.BEST_OF: optional "bo" prefix + 1-2 digits).
/// This is the first place a real chat command flips MatchSession.IsScrimming/WinningPoints —
/// everything else (MatchScoringService, ScrimParticipant, match points/winners/bans) was built
/// in an earlier commit but had no player-facing way to turn it on until this one.
/// </summary>
public sealed partial class MpScrimCommand : IMpSubCommand
{
    public string Trigger => "scrim";

    public IReadOnlyList<string> Aliases => ["autoref"];

    public Privileges RequiredPriv => Privileges.Unrestricted;

    public bool Hidden => false;

    public string? Description => "Start a scrim in the current match.";

    public async Task<string?> HandleAsync(MpCommandContext ctx)
    {
        if (ctx.Args.Count != 1)
        {
            return "Invalid syntax: !mp scrim <bo#>";
        }

        var bestOfMatch = BestOfRegex().Match(ctx.Args[0]);
        if (!bestOfMatch.Success)
        {
            return "Invalid syntax: !mp scrim <bo#>";
        }

        var bestOf = int.Parse(bestOfMatch.Groups[1].Value);
        if (bestOf is < 0 or >= 16)
        {
            return "Best of must be in range 0-15.";
        }

        var winningPoints = (bestOf / 2) + 1;
        var match = ctx.Match;

        await match.Lock.WaitAsync();
        try
        {
            string message;
            if (winningPoints != 0)
            {
                if (match.IsScrimming)
                {
                    return "Already scrimming!";
                }

                if (bestOf % 2 == 0)
                {
                    return "Best of must be an odd number!";
                }

                match.IsScrimming = true;
                message = $"A scrimmage has been started by {ctx.Player.Name}; first to {winningPoints} points wins. Best of luck!";
            }
            else
            {
                if (!match.IsScrimming)
                {
                    return "Not currently scrimming!";
                }

                match.IsScrimming = false;
                match.ResetScrim();
                message = "Scrimming cancelled.";
            }

            match.WinningPoints = winningPoints;
            return message;
        }
        finally
        {
            match.Lock.Release();
        }
    }

    [GeneratedRegex(@"^(?:bo)?(\d{1,2})$")]
    private static partial Regex BestOfRegex();
}
