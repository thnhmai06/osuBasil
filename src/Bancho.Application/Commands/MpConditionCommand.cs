using Bancho.Application.UseCases.Multiplayer;
using Bancho.Domain;

namespace Bancho.Application.Commands;

/// <summary>
/// Ported from app/commands.py's mp_condition. Drops the "pp" special case entirely (Python
/// allows "pp" as a scrim-only win condition via a use_pp_scoring flag) per the project's no-pp
/// scope decision — every scrim always scores by (score, acc, max_combo, score)[win_condition].
/// </summary>
public sealed class MpConditionCommand(MatchMembershipService matchMembership) : IMpSubCommand
{
    public string Trigger => "condition";

    public IReadOnlyList<string> Aliases => ["cond"];

    public Privileges RequiredPriv => Privileges.Unrestricted;

    public bool Hidden => false;

    public string? Description => "Change the win condition for the match.";

    public async Task<string?> HandleAsync(MpCommandContext ctx)
    {
        if (ctx.Args.Count != 1)
        {
            return "Invalid syntax: !mp condition <type>";
        }

        if (ctx.Args[0] == "pp")
        {
            return "PP is not supported as a win condition on this server.";
        }

        MatchWinConditions? winCondition = ctx.Args[0] switch
        {
            "score" => MatchWinConditions.Score,
            "accuracy" or "acc" => MatchWinConditions.Accuracy,
            "combo" => MatchWinConditions.Combo,
            "scorev2" or "v2" => MatchWinConditions.ScoreV2,
            _ => null,
        };

        if (winCondition is null)
        {
            return "Invalid win condition. (score, acc, combo, scorev2)";
        }

        var match = ctx.Match;
        await match.Lock.WaitAsync();
        try
        {
            match.WinCondition = winCondition.Value;
            matchMembership.EnqueueState(match, lobby: false);
        }
        finally
        {
            match.Lock.Release();
        }

        return "Match win condition updated.";
    }
}
