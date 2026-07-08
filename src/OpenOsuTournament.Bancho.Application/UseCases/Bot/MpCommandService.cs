using OpenOsuTournament.Bancho.Application.Abstractions;
using OpenOsuTournament.Bancho.Application.Abstractions.Beatmaps;
using OpenOsuTournament.Bancho.Application.Abstractions.Multiplayer;
using OpenOsuTournament.Bancho.Application.Sessions;
using OpenOsuTournament.Bancho.Application.Sessions.Multiplayer;
using OpenOsuTournament.Bancho.Application.UseCases.Multiplayer;
using OpenOsuTournament.Bancho.Domain;
using OpenOsuTournament.Bancho.Domain.Multiplayer;
using OpenOsuTournament.Bancho.Protocol.Packets;

namespace OpenOsuTournament.Bancho.Application.UseCases.Bot;

/// <summary>
///     `!mp` subcommands. Ported loosely from bancho.py's mp_commands — verified against the actual
///     source (not the plan doc's guessed list, which included commands like make/close that don't
///     exist there: matches are created via the MATCH_CREATE packet, not a chat command, and there is
///     no close command). Deliberately NOT ported: `!mp make` (no packet-level creation path to wrap),
///     `!mp timer`/`aborttimer` (needs a background scheduler — none exists yet), and every
///     scrim/mappool command (scrim engine was deleted wholesale — see docs/scope-decisions.md, not
///     resurrected here without being asked).
///     Every subcommand except `help` requires <see cref="MatchSession.IsReferee" />, matching
///     bancho.py's ensure_match check — unmet permission is a silent no-op (no error reply), not an
///     upgraded error message.
/// </summary>
public sealed class MpCommandService(
    MatchMembershipService matchMembership,
    IMatchPersistenceRepository matchPersistence,
    IMapRepository mapRepository,
    IPlayerSessionRegistry sessionRegistry,
    IClock clock)
{
    private const int MaxMatchNameLength = 50;

    private static readonly IReadOnlyDictionary<string, MatchTeamTypes> TeamTypeAliases = new Dictionary<string, MatchTeamTypes>(StringComparer.OrdinalIgnoreCase)
    {
        ["ffa"] = MatchTeamTypes.HeadToHead,
        ["headtohead"] = MatchTeamTypes.HeadToHead,
        ["tag"] = MatchTeamTypes.TagCoop,
        ["tagcoop"] = MatchTeamTypes.TagCoop,
        ["teamvs"] = MatchTeamTypes.TeamVs,
        ["teams"] = MatchTeamTypes.TeamVs,
        ["tagteamvs"] = MatchTeamTypes.TagTeamVs,
        ["tag-teams"] = MatchTeamTypes.TagTeamVs
    };

    private static readonly IReadOnlyDictionary<string, MatchWinConditions> WinConditionAliases = new Dictionary<string, MatchWinConditions>(StringComparer.OrdinalIgnoreCase)
    {
        ["score"] = MatchWinConditions.Score,
        ["acc"] = MatchWinConditions.Accuracy,
        ["accuracy"] = MatchWinConditions.Accuracy,
        ["combo"] = MatchWinConditions.Combo,
        ["scorev2"] = MatchWinConditions.ScoreV2,
        ["v2"] = MatchWinConditions.ScoreV2
    };

    public async Task<string?> HandleAsync(PlayerSession sender, MatchSession match, string subcommand,
        IReadOnlyList<string> args, CancellationToken cancellationToken = default)
    {
        if (subcommand is "" or "help") return HelpText;

        if (!match.IsReferee(sender.Id)) return null;

        return subcommand switch
        {
            "settings" => Settings(match),
            "lock" => await RunLockedAsync(match, () => Task.FromResult(LockSlot(match, args, locked: true))),
            "unlock" => await RunLockedAsync(match, () => Task.FromResult(LockSlot(match, args, locked: false))),
            "size" => await RunLockedAsync(match, () => Task.FromResult(SetSize(match, args))),
            "move" => await RunLockedAsync(match, () => Task.FromResult(MoveSlot(match, args))),
            "host" => await RunLockedAsync(match, () => Task.FromResult(SetHost(match, args))),
            "clearhost" => await RunLockedAsync(match, () => Task.FromResult(ClearHost(match))),
            "name" => await RunLockedAsync(match, () => Task.FromResult(SetName(match, args))),
            "password" => await RunLockedAsync(match, () => Task.FromResult(SetPassword(match, args))),
            "invite" => Invite(sender, match, args),
            "addref" => AddReferee(sender, match, args),
            "removeref" => RemoveReferee(sender, match, args),
            "listref" => ListReferees(match),
            "team" => await RunLockedAsync(match, () => Task.FromResult(SetTeam(match, args))),
            "teams" => await RunLockedAsync(match, () => Task.FromResult(SetTeamType(match, args))),
            "condition" or "cond" => await RunLockedAsync(match, () => Task.FromResult(SetWinCondition(match, args))),
            "map" => await RunLockedAsync(match, () => SetMapAsync(match, args, cancellationToken)),
            "mods" => await RunLockedAsync(match, () => Task.FromResult(SetMods(match, args))),
            "freemods" => await RunLockedAsync(match, () => Task.FromResult(SetFreemods(match, args))),
            "start" => await RunLockedAsync(match, () => StartAsync(match, args, cancellationToken)),
            "abort" => await RunLockedAsync(match, () => AbortAsync(match, cancellationToken)),
            "kick" => await RunLockedAsync(match, () => Task.FromResult(Kick(match, args))),
            _ => null
        };
    }

    private const string HelpText =
        "!mp: settings, lock <slot>, unlock <slot>, size <n>, move <name> <slot>, host <name>, " +
        "clearhost, name <text>, password [text|randpw], invite <name>, addref <name>, removeref <name>, " +
        "listref, team <name> <red|blue>, teams <ffa|tag|teamvs|tagteamvs>, condition <score|acc|combo|scorev2>, " +
        "map <id>, mods <acronyms>, freemods <on|off>, start, abort, kick <name>";

    private static async Task<string?> RunLockedAsync(MatchSession match, Func<Task<string?>> action)
    {
        await match.Lock.WaitAsync();
        try
        {
            return await action();
        }
        finally
        {
            match.Lock.Release();
        }
    }

    private static string Settings(MatchSession match)
    {
        var slots = string.Join(", ", match.Slots
            .Select((slot, i) => (slot, i))
            .Where(t => !t.slot.Empty)
            .Select(t => $"#{t.i + 1} {t.slot.PlayerId}{(t.i == match.GetSlotId(match.HostId) ? " (host)" : "")}"));

        return $"{match.Name} | map: {match.MapName} | teams: {match.TeamType} | condition: {match.WinCondition} | " +
               $"mods: {match.Mods} | freemods: {match.Freemods} | players: {(slots.Length == 0 ? "none" : slots)}";
    }

    private string? LockSlot(MatchSession match, IReadOnlyList<string> args, bool locked)
    {
        if (!TryParseSlotIndex(args, out var slotId)) return "Usage: !mp lock <slot 1-16>";

        var slot = match.Slots[slotId];
        if (locked)
        {
            if (!slot.Empty) return "Can't lock an occupied slot.";
            slot.Status = SlotStatus.Locked;
        }
        else
        {
            if (slot.Status == SlotStatus.Locked) slot.Status = SlotStatus.Open;
        }

        matchMembership.EnqueueState(match);
        return $"Slot {slotId + 1} {(locked ? "locked" : "unlocked")}.";
    }

    private string? SetSize(MatchSession match, IReadOnlyList<string> args)
    {
        if (args.Count < 1 || !int.TryParse(args[0], out var size) || size is < 1 or > 16)
            return "Usage: !mp size <1-16>";

        for (var i = 0; i < 16; i++)
        {
            var slot = match.Slots[i];
            if (!slot.Empty) continue;

            if (i >= size && slot.Status == SlotStatus.Open) slot.Status = SlotStatus.Locked;
            else if (i < size && slot.Status == SlotStatus.Locked) slot.Status = SlotStatus.Open;
        }

        matchMembership.EnqueueState(match);
        return $"Match size set to {size}.";
    }

    private string? MoveSlot(MatchSession match, IReadOnlyList<string> args)
    {
        if (args.Count < 2 || !int.TryParse(args[^1], out var destSlotId) || destSlotId is < 1 or > 16)
            return "Usage: !mp move <name> <slot 1-16>";

        var targetName = string.Join(' ', args.Take(args.Count - 1));
        var target = sessionRegistry.GetByName(targetName);
        if (target is null || target.Match != match) return $"{targetName} is not in this match.";

        destSlotId--;
        var destSlot = match.Slots[destSlotId];
        if (destSlot.Status != SlotStatus.Open) return "Destination slot is not open.";

        var sourceSlot = match.GetSlot(target.Id);
        if (sourceSlot is null) return $"{targetName} is not in this match.";

        destSlot.CopyFrom(sourceSlot);
        sourceSlot.Reset();
        matchMembership.EnqueueState(match);
        return $"Moved {target.Name} to slot {destSlotId + 1}.";
    }

    private string? SetHost(MatchSession match, IReadOnlyList<string> args)
    {
        if (args.Count < 1) return "Usage: !mp host <name>";

        var targetName = string.Join(' ', args);
        var target = sessionRegistry.GetByName(targetName);
        if (target is null || target.Match != match) return $"{targetName} is not in this match.";

        match.HostId = target.Id;
        target.Enqueue(ServerPacketWriter.MatchTransferHost());
        matchMembership.EnqueueState(match);
        return $"{target.Name} is now the host.";
    }

    private string? ClearHost(MatchSession match)
    {
        match.HostId = 0;
        matchMembership.EnqueueState(match);
        return "Host cleared.";
    }

    private string? SetName(MatchSession match, IReadOnlyList<string> args)
    {
        if (args.Count < 1) return "Usage: !mp name <text>";

        var name = string.Join(' ', args);
        if (name.Length > MaxMatchNameLength) name = name[..MaxMatchNameLength];

        match.Name = name;
        matchMembership.EnqueueState(match);
        return $"Match renamed to {name}.";
    }

    private string? SetPassword(MatchSession match, IReadOnlyList<string> args)
    {
        if (args.Count == 0 || args[0] is "off" or "none")
        {
            match.Password = "";
            matchMembership.EnqueueState(match);
            return "Match password removed.";
        }

        if (args[0] == "randpw")
        {
            match.Password = Guid.NewGuid().ToString("N")[..8];
            matchMembership.EnqueueState(match);
            return "Match password randomized.";
        }

        match.Password = string.Join(' ', args);
        matchMembership.EnqueueState(match);
        return "Match password set.";
    }

    private string? Invite(PlayerSession sender, MatchSession match, IReadOnlyList<string> args)
    {
        if (args.Count < 1) return "Usage: !mp invite <name>";

        var targetName = string.Join(' ', args);
        var target = sessionRegistry.GetByName(targetName);
        if (target is null) return $"{targetName} is not online.";

        target.Enqueue(ServerPacketWriter.MatchInvite(sender.Id, sender.Name, match.Embed, target.Name));
        return $"Invited {target.Name}.";
    }

    private string? AddReferee(PlayerSession sender, MatchSession match, IReadOnlyList<string> args)
    {
        if (sender.Id != match.HostId) return null;
        if (args.Count < 1) return "Usage: !mp addref <name>";

        var targetName = string.Join(' ', args);
        var target = sessionRegistry.GetByName(targetName);
        if (target is null) return $"{targetName} is not online.";

        match.AddReferee(target.Id);
        return $"{target.Name} added as a referee.";
    }

    private string? RemoveReferee(PlayerSession sender, MatchSession match, IReadOnlyList<string> args)
    {
        if (sender.Id != match.HostId) return null;
        if (args.Count < 1) return "Usage: !mp removeref <name>";

        var targetName = string.Join(' ', args);
        var target = sessionRegistry.GetByName(targetName);
        if (target is null) return $"{targetName} is not online.";

        match.RemoveReferee(target.Id);
        return $"{target.Name} removed as a referee.";
    }

    private string? ListReferees(MatchSession match)
    {
        if (match.Referees.Count == 0) return "No extra referees (host is always a referee).";

        var names = match.Referees
            .Select(id => sessionRegistry.GetById(id)?.Name ?? $"#{id}")
            .ToList();
        return $"Referees: {string.Join(", ", names)}";
    }

    private string? SetTeam(MatchSession match, IReadOnlyList<string> args)
    {
        if (args.Count < 2) return "Usage: !mp team <name> <red|blue>";

        var teamArg = args[^1].ToLowerInvariant();
        if (teamArg is not ("red" or "blue")) return "Usage: !mp team <name> <red|blue>";

        var targetName = string.Join(' ', args.Take(args.Count - 1));
        var target = sessionRegistry.GetByName(targetName);
        var slot = target is not null ? match.GetSlot(target.Id) : null;
        if (slot is null) return $"{targetName} is not in this match.";

        slot.Team = teamArg == "red" ? MatchTeams.Red : MatchTeams.Blue;
        matchMembership.EnqueueState(match, false);
        return $"{target!.Name} moved to {teamArg}.";
    }

    private string? SetTeamType(MatchSession match, IReadOnlyList<string> args)
    {
        if (args.Count < 1 || !TeamTypeAliases.TryGetValue(args[0], out var newType))
            return "Usage: !mp teams <ffa|tag|teamvs|tagteamvs>";

        if (match.TeamType != newType)
        {
            var newTeam = newType is MatchTeamTypes.HeadToHead or MatchTeamTypes.TagCoop
                ? MatchTeams.Neutral
                : MatchTeams.Red;

            foreach (var slot in match.Slots)
                if (slot.PlayerId is not null)
                    slot.Team = newTeam;

            match.TeamType = newType;
        }

        matchMembership.EnqueueState(match);
        return $"Team type set to {newType}.";
    }

    private string? SetWinCondition(MatchSession match, IReadOnlyList<string> args)
    {
        if (args.Count < 1 || !WinConditionAliases.TryGetValue(args[0], out var condition))
            return "Usage: !mp condition <score|acc|combo|scorev2>";

        match.WinCondition = condition;
        matchMembership.EnqueueState(match);
        return $"Win condition set to {condition}.";
    }

    private async Task<string?> SetMapAsync(MatchSession match, IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (args.Count < 1 || !int.TryParse(args[0], out var beatmapId)) return "Usage: !mp map <beatmap id>";

        var bmap = await mapRepository.FetchOneAsync(beatmapId, cancellationToken: cancellationToken);
        if (bmap is null) return $"No beatmap with id {beatmapId} found locally.";

        match.UnreadyPlayers();
        match.MapId = bmap.Id;
        match.MapMd5 = bmap.Md5;
        match.MapName = bmap.FullName;
        match.Mode = bmap.Mode;
        matchMembership.EnqueueState(match);
        return $"Map changed to {bmap.FullName}.";
    }

    private string? SetMods(MatchSession match, IReadOnlyList<string> args)
    {
        if (args.Count < 1) return "Usage: !mp mods <acronyms>";
        if (match.Freemods) return "Match is in freemod — per-player mods can't be set with !mp mods.";

        match.Mods = ModsExtensions.FromModString(args[0]);
        matchMembership.EnqueueState(match);
        return $"Mods set to {match.Mods}.";
    }

    private string? SetFreemods(MatchSession match, IReadOnlyList<string> args)
    {
        if (args.Count < 1 || args[0] is not ("on" or "off")) return "Usage: !mp freemods <on|off>";

        var freemods = args[0] == "on";
        if (freemods == match.Freemods) return $"Freemods already {args[0]}.";

        match.Freemods = freemods;
        if (freemods)
        {
            foreach (var slot in match.Slots)
                if (slot.PlayerId is not null)
                    slot.Mods = match.Mods & ~ModsExtensions.SpeedChangingMods;

            match.Mods &= ModsExtensions.SpeedChangingMods;
        }
        else
        {
            var hostSlot = match.GetHostSlot();
            match.Mods &= ModsExtensions.SpeedChangingMods;
            if (hostSlot is not null) match.Mods |= hostSlot.Mods;

            foreach (var slot in match.Slots)
                if (slot.PlayerId is not null)
                    slot.Mods = Mods.NoMod;
        }

        matchMembership.EnqueueState(match);
        return $"Freemods {args[0]}.";
    }

    private async Task<string?> StartAsync(MatchSession match, IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (match.InProgress) return "Match is already in progress.";

        await matchMembership.StartAsync(match, cancellationToken);
        return args.Count > 0 && int.TryParse(args[0], out _)
            ? "Countdown timers aren't supported yet — starting now."
            : "Match started.";
    }

    private async Task<string?> AbortAsync(MatchSession match, CancellationToken cancellationToken)
    {
        if (!match.InProgress) return "Match is not in progress.";

        match.UnreadyPlayers(SlotStatus.Playing);
        match.ResetPlayersLoadedStatus();
        match.InProgress = false;

        if (match.CurrentRoundId is { } roundId)
        {
            await matchPersistence.SetRoundEndedAsync(roundId, clock.UtcNow.UtcDateTime, cancellationToken);
            match.CurrentRoundId = null;
        }

        matchMembership.Enqueue(match, ServerPacketWriter.MatchAbort(), false);
        matchMembership.EnqueueState(match);
        return "Match aborted.";
    }

    private string? Kick(MatchSession match, IReadOnlyList<string> args)
    {
        if (args.Count < 1) return "Usage: !mp kick <name>";

        var targetName = string.Join(' ', args);
        var target = sessionRegistry.GetByName(targetName);
        if (target is null || target.Match != match) return $"{targetName} is not in this match.";
        if (target.Id == match.HostId) return "Can't kick the host.";

        matchMembership.Leave(target, match);
        target.Enqueue(ServerPacketWriter.MatchJoinFail());
        return $"Kicked {target.Name}.";
    }

    private static bool TryParseSlotIndex(IReadOnlyList<string> args, out int slotId)
    {
        slotId = -1;
        if (args.Count < 1 || !int.TryParse(args[0], out var oneBased) || oneBased is < 1 or > 16) return false;

        slotId = oneBased - 1;
        return true;
    }
}
