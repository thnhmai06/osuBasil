using Bancho.Application.UseCases.Multiplayer;
using Bancho.Domain;

namespace Bancho.Application.Commands;

/// <summary>Ported from app/commands.py's mp_teams.</summary>
public sealed class MpTeamsCommand(MatchMembershipService matchMembership) : IMpSubCommand
{
    public string Trigger => "teams";

    public IReadOnlyList<string> Aliases => [];

    public Privileges RequiredPriv => Privileges.Unrestricted;

    public bool Hidden => false;

    public string? Description => "Change the team type for the current match.";

    public async Task<string?> HandleAsync(MpCommandContext ctx)
    {
        if (ctx.Args.Count != 1)
        {
            return "Invalid syntax: !mp teams <type>";
        }

        MatchTeamTypes? teamType = ctx.Args[0] switch
        {
            "ffa" or "freeforall" or "head-to-head" => MatchTeamTypes.HeadToHead,
            "tag" or "coop" or "co-op" or "tag-coop" => MatchTeamTypes.TagCoop,
            "teams" or "team-vs" or "teams-vs" => MatchTeamTypes.TeamVs,
            "tag-teams" or "tag-team-vs" or "tag-teams-vs" => MatchTeamTypes.TagTeamVs,
            _ => null,
        };

        if (teamType is null)
        {
            return "Unknown team type. (ffa, tag, teams, tag-teams)";
        }

        var match = ctx.Match;
        await match.Lock.WaitAsync();
        try
        {
            match.TeamType = teamType.Value;

            var newTeam = teamType is MatchTeamTypes.HeadToHead or MatchTeamTypes.TagCoop
                ? MatchTeams.Neutral
                : MatchTeams.Red;

            foreach (var slot in match.Slots)
            {
                if (slot.PlayerId is not null)
                {
                    slot.Team = newTeam;
                }
            }

            if (match.IsScrimming)
            {
                match.ResetScrim();
            }

            matchMembership.EnqueueState(match);
        }
        finally
        {
            match.Lock.Release();
        }

        return "Match team type updated.";
    }
}
