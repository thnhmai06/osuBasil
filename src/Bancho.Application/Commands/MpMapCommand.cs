using Bancho.Application.Abstractions;
using Bancho.Application.Configuration;
using Bancho.Application.UseCases.Multiplayer;
using Bancho.Domain;
using Microsoft.Extensions.Options;

namespace Bancho.Application.Commands;

/// <summary>Ported from app/commands.py's mp_map. Fetches the beatmap before acquiring the match lock, matching the no-I/O-under-lock rule used throughout MatchScoringService.</summary>
public sealed class MpMapCommand(IMapRepository maps, MatchMembershipService matchMembership, IOptions<ServerBehaviorOptions> serverOptions) : IMpSubCommand
{
    public string Trigger => "map";

    public IReadOnlyList<string> Aliases => [];

    public Privileges RequiredPriv => Privileges.Unrestricted;

    public bool Hidden => false;

    public string? Description => "Set the current match's current map by id.";

    public async Task<string?> HandleAsync(MpCommandContext ctx)
    {
        if (ctx.Args.Count != 1 || !int.TryParse(ctx.Args[0], out var mapId))
        {
            return "Invalid syntax: !mp map <beatmapid>";
        }

        if (mapId == ctx.Match.MapId)
        {
            return "Map already selected.";
        }

        var bmap = await maps.FetchOneAsync(id: mapId);
        if (bmap is null)
        {
            return "Beatmap not found.";
        }

        var match = ctx.Match;
        await match.Lock.WaitAsync();
        try
        {
            match.MapId = bmap.Id;
            match.MapMd5 = bmap.Md5;
            match.MapName = bmap.FullName;
            match.Mode = bmap.Mode;
            matchMembership.EnqueueState(match);
        }
        finally
        {
            match.Lock.Release();
        }

        var domain = serverOptions.Value.Domain;
        return $"Selected: [https://osu.{domain}/b/{bmap.Id} {bmap.FullName}].";
    }
}
