using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Abstractions.Users;
using Basil.Application.PacketHandlers.Channels;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Protocol.Multiplayer;
using Basil.Protocol.Packets;

namespace Basil.Application.Services.Bot;

/// <summary>
///     `!mp` subcommands, matched against real osu! Bancho's own wiki/chat behaviour rather than
///     bancho.py — see docs/working-scopes.md for what's a deliberate Basil-only addition vs a real
///     Bancho command. <c>makeprivate</c> is no longer an alias of <c>make</c> — it now redirects to
///     <see cref="SetPrivate" /> (setting the room private), and <c>!mp private [0|1]</c> is the
///     canonical way to view/change privacy. <c>!mp join &lt;id&gt;</c> bypasses the referee gate
///     (routed from <see cref="CommandDispatcher" /> directly). Every subcommand except
///     <c>help</c>/<c>make</c>/<c>join</c>/<c>in</c> requires <see cref="MatchSession.IsReferee" /> —
///     unmet permission is a silent no-op (no error reply), not an upgraded error message. Referee is
///     a pure permission flag (doesn't require physical presence in the room); the host is NOT
///     automatically a referee — hosting only grants direct in-client settings control, which ranks
///     below referee authority for `!mp` purposes.
///
///     The actual room-mutation logic lives in <see cref="MatchControlService" /> — shared with the
///     `api.` host's HTTP write routes so both surfaces call the identical state-mutation/broadcast
///     code. This class owns everything chat-specific: parsing raw argument tokens, resolving a
///     target player by name (<see cref="IPlayerSessionRegistry.GetByName" />), the referee gate, and
///     formatting a reply string from the result.
/// </summary>
public sealed class MpCommandService(
    MatchMembershipService matchMembership,
    IMatchRegistry matchRegistry,
    IMatchPersistenceRepository matchPersistence,
    IMapRepository mapRepository,
    IPlayerSessionRegistry sessionRegistry,
    IUserRepository userRepository)
{
    private const int MaxMatchNameLength = 50;

    private readonly MatchControlService _matchControl =
        new(matchMembership, matchPersistence, mapRepository, sessionRegistry);

    /// <summary>
    ///     Single source of truth for `!mp help`'s output — add a subcommand here, and it shows up with
    ///     no separate help string to keep in sync. `make`/`makeprivate`/`in` are listed in
    ///     <see cref="CommandDispatcher" />'s own help instead, since they run outside this class'
    ///     switch (see <see cref="TryHandleAsync" />'s doc comment).
    /// </summary>
    private static readonly CommandInfo[] Commands =
    [
        new("!mp settings", "show match id, map, team type, win condition, mods, and slots"),
        new("!mp lock", "lock the room, blocking new joins"),
        new("!mp unlock", "unlock the room"),
        new("!mp private [0|1]", "show or set the room's private status (hidden from lobby, invite-only)"),
        new("!mp size <1-16>", "set the number of available slots"),
        new("!mp move <name> <slot 1-16>", "move a player to another slot"),
        new("!mp host <name>", "transfer host to another player"),
        new("!mp clearhost", "clear the current host"),
        new("!mp name <text>", "rename the match"),
        new("!mp password [text]", "set the room password; omit to clear it"),
        new("!mp invite <name>", "invite an online player"),
        new("!mp addref <name>", "add a referee"),
        new("!mp removeref <name>", "remove a referee"),
        new("!mp listrefs", "list current referees"),
        new("!mp banlist", "list players banned from this match"),
        new("!mp team <name> <red|blue>", "assign a player's team\nTeam: Red, Blue"),
        new("!mp map <beatmap id>", "change the selected map"),
        new("!mp mods <mods>|Freemod|None",
            "set the match mods\nMods: NF, EZ, HD, HR, SD, DT, RX, HT, NC, FL, SO, AP, PF, Freemod, None"),
        new("!mp set <teammode 0-3> [scoremode 0-3] [size 1-16]",
            "set team type, win condition, and size at once\n" +
            "Teammode 0: HeadToHead, 1: TagCoop, 2: TeamVs, 3: TagTeamVs\n" +
            "Scoremode 0: Score, 1: Accuracy, 2: Combo, 3: ScoreV2"),
        new("!mp start [seconds]", "start now, or start a countdown"),
        new("!mp timer [seconds]", "start a countdown without auto-starting"),
        new("!mp aborttimer", "cancel a running countdown"),
        new("!mp abort", "abort the match in progress"),
        new("!mp kick <name>", "remove a player from the room"),
        new("!mp ban <name>", "kick and block a player from rejoining"),
        new("!mp unban <name>", "allow a banned player to rejoin"),
        new("!mp close", "close the match immediately")
    ];

    private static readonly string HelpText = string.Join('\n', Commands.Select(c => $"{c.Usage} - {c.Description}"));

    public async Task<string?> HandleAsync(PlayerSession sender, MatchSession match, string subcommand,
        IReadOnlyList<string> args, CancellationToken cancellationToken = default)
    {
        var (_, reply) = await TryHandleAsync(sender, match, subcommand, args, cancellationToken);
        return reply;
    }

    /// <summary>
    ///     Same dispatch as <see cref="HandleAsync" />, but also reports whether the subcommand actually
    ///     succeeded — used by <see cref="CommandDispatcher" /> to gate `&amp;&amp;`-chained `!mp` commands.
    ///     A usage error, a permission rejection (silent `null` reply), or any other rejected precondition
    ///     counts as a failure; every state-changing/informational reply counts as success.
    /// </summary>
    public async Task<(bool Success, string? Reply)> TryHandleAsync(PlayerSession sender, MatchSession match,
        string subcommand, IReadOnlyList<string> args, CancellationToken cancellationToken = default)
    {
        if (subcommand is "" or "help") return (true, HelpText);

        if (!match.IsReferee(sender.Id)) return (false, null);

        return subcommand switch
        {
            "settings" => (true, await SettingsAsync(match, cancellationToken)),
            "lock" => await RunLockedAsync(match, () => Task.FromResult((true, SetRoomLocked(match, true)))),
            "unlock" => await RunLockedAsync(match, () => Task.FromResult((true, SetRoomLocked(match, false)))),
            "private" => (true, SetPrivate(match, args)),
            "makeprivate" => (true, SetPrivate(match, ["1"])),
            "size" => await RunLockedAsync(match, () => Task.FromResult(SetSize(match, args))),
            "move" => await RunLockedAsync(match, () => Task.FromResult(MoveSlot(match, args))),
            "host" => await RunLockedAsync(match, () => Task.FromResult(SetHost(match, args))),
            "clearhost" => await RunLockedAsync(match, () => Task.FromResult((true, ClearHost(match)))),
            "name" => await RunLockedAsync(match, () => Task.FromResult(SetName(match, args))),
            "password" => await RunLockedAsync(match, () => Task.FromResult((true, SetPassword(match, args)))),
            "invite" => Invite(sender, match, args),
            "addref" => await AddRefereeAsync(sender, match, args, cancellationToken),
            "removeref" => await RunLockedAsync(match, () => RemoveRefereeAsync(sender, match, args, cancellationToken)),
            "listrefs" => (true, ListReferees(match)),
            "banlist" => (true, await BanListAsync(match, cancellationToken)),
            "team" => await RunLockedAsync(match, () => Task.FromResult(SetTeam(match, args))),
            "set" => await RunLockedAsync(match, () => Task.FromResult(Set(match, args))),
            "map" => await RunLockedAsync(match, () => SetMapAsync(match, args, cancellationToken)),
            "mods" => await RunLockedAsync(match, () => Task.FromResult(SetMods(match, args))),
            "start" => await RunLockedAsync(match, () => StartAsync(match, args, cancellationToken)),
            "timer" => await RunLockedAsync(match, () => Task.FromResult(Timer(match, args))),
            "aborttimer" => await RunLockedAsync(match, () => Task.FromResult(AbortTimer(match))),
            "abort" => await RunLockedAsync(match, () => AbortAsync(match, cancellationToken)),
            "kick" => await RunLockedAsync(match, () => KickAsync(sender, match, args, cancellationToken)),
            "ban" => await RunLockedAsync(match, () => BanAsync(sender, match, args, cancellationToken)),
            "unban" => await RunLockedAsync(match, () => UnbanAsync(match, args, cancellationToken)),
            "close" => await RunLockedAsync(match, () => AlwaysSucceeds(CloseAsync(sender, match, cancellationToken))),
            _ => (false, null)
        };
    }

    /// <summary>
    ///     Backs `!mp make`/`!mp makeprivate` — one shared implementation, no `isPrivate` flag or any
    ///     other parameter distinguishes the two trigger words (see <see cref="CommandDispatcher" />).
    ///     Unlike every other subcommand this runs with no <see cref="MatchSession" /> yet (there's
    ///     nothing to be a referee of), so it bypasses <see cref="HandleAsync" /> entirely — reusing
    ///     <see cref="MatchMembershipService.CreateAsync" /> verbatim, exactly like a real
    ///     `MATCH_CREATE` packet, except the creator is also auto-added as a referee (required to
    ///     bootstrap — nobody could otherwise ever pass the outer <see cref="MatchSession.IsReferee" />
    ///     gate on their own brand-new room). Marked <see cref="MatchSession.CreatedViaMakeCommand" />
    ///     so it persists until `!mp close` or the referee list empties, instead of auto-tearing down
    ///     once every slot empties like a normal client-created room.
    /// </summary>
    public async Task<string?> MakeAsync(PlayerSession sender, IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        var name = args.Count > 0 ? string.Join(' ', args) : $"{sender.Name}'s match";
        if (name.Length > MaxMatchNameLength) name = name[..MaxMatchNameLength];

        var data = new ReadMatchResult(
            0, false, 0, 0, name, "",
            "", 0, "",
            [], [], [], sender.Id, 0,
            0, 0, false, [], 0);

        var match = await matchMembership.CreateAsync(sender, data, cancellationToken, true);
        if (match is null) return "Couldn't create the match — server is full.";

        match.AddReferee(sender.Id);
        sender.MpScopeMatchId = match.DbId;
        return
            $"Created the match #{match.DbId} {match.Name}. You are now scoped to this match, and added as a referee.";
    }

    /// <summary>
    ///     Backs `!mp join &lt;id&gt; [password]` — lets any player join a match by its wire-format id
    ///     (0-63). A private match rejects everyone but staff and invitees, host included (see
    ///     <see cref="MatchSession.IsPrivate" />); locked rooms and banned players are rejected too,
    ///     with a descriptive message. Runs with no <see cref="MatchSession" /> scope (routed directly
    ///     from <see cref="CommandDispatcher" />, bypassing scope resolution).
    /// </summary>
    public async Task<string?> JoinAsync(PlayerSession sender, IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        if (args.Count < 1 || !int.TryParse(args[0], out var matchId))
            return "Usage: !mp join <id> [password]";

        var match = matchRegistry.GetById(matchId);
        if (match is null) return $"No active match with id {matchId}.";
        if (match.IsPrivate && (sender.Priv & UserPrivileges.Staff) == 0 && !match.InvitedIds.Contains(sender.Id))
            return $"Cannot join match #{matchId} — the room is private. Ask a referee for an invite.";
        if (sender.Match is not null) return "You're already in a match.";
        if (match.BannedIds.Contains(sender.Id)) return "You're banned from this match.";

        await match.Lock.WaitAsync(cancellationToken);
        try
        {
            var password = args.Count > 1 ? string.Join(' ', args.Skip(1)) : "";
            return matchMembership.Join(sender, match, password)
                ? $"Joined match #{matchId} {match.Name}"
                : "Failed to join the match.";
        }
        finally
        {
            match.Lock.Release();
        }
    }

    /// <summary>
    ///     Backs `!mp in [match_id]` — lets a referee target a match they aren't physically joined to,
    ///     so `CommandDispatcher` can resolve `!mp` scope from <see cref="PlayerSession.MpScopeMatchId" />
    ///     instead of the sender's current chat channel. No argument reports the current scope instead of
    ///     setting one. Runs with no <see cref="MatchSession" /> yet, like `make`/`makeprivate`, since the
    ///     whole point is reaching a match the sender isn't in.
    /// </summary>
    public string SetScopeAsync(PlayerSession sender, IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            if (sender.MpScopeMatchId is not { } currentId) return "You're not scoped to any match.";

            var current = matchRegistry.GetByDbId(currentId);
            return current is null
                ? $"You were scoped to match #{currentId}, but it no longer exists."
                : $"Currently scoped to match #{current.DbId} {current.Name}.";
        }

        if (!int.TryParse(args[0], out var dbId)) return "Usage: !mp in [match_id]";

        var match = matchRegistry.GetByDbId(dbId);
        if (match is null) return $"No active match with id #{dbId}.";
        if (!match.IsReferee(sender.Id)) return $"You're not a referee of match #{dbId}.";

        sender.MpScopeMatchId = dbId;
        return $"Now targeting match #{dbId} {match.Name}.";
    }

    private static async Task<(bool Success, string? Reply)> AlwaysSucceeds(Task<string?> reply)
    {
        return (true, await reply);
    }

    private static async Task<(bool Success, string? Reply)> RunLockedAsync(MatchSession match,
        Func<Task<(bool Success, string? Reply)>> action)
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

    /// <summary>
    ///     Ported to match real Bancho's `!mp settings` output shape (one field per line, so each
    ///     becomes its own chat message — see <see cref="SendPublicMessageHandler" />'s reply-splitting).
    ///     Real Bancho links the room's `osu.ppy.sh/mp/{id}` history page and each player's profile —
    ///     Basil has neither (no public match-history page, no profile pages), so those are plain
    ///     text/IDs here instead of links.
    /// </summary>
    private async Task<string> SettingsAsync(MatchSession match, CancellationToken cancellationToken)
    {
        var beatmapLine = match.MapId > 0
            ? await mapRepository.FetchOneAsync(id: match.MapId, cancellationToken: cancellationToken) is { } bmap
                ? $"Beatmap: {bmap.Id} {bmap.FullName}"
                : "Beatmap: Not found"
            : $"Beatmap: {match.MapId} {match.MapName}";

        var lines = new List<string>
        {
            $"Room name: {match.Name} (#{match.DbId})",
            beatmapLine,
            $"Team mode: {match.TeamType}, Win condition: {match.WinCondition}"
        };

        var activeMods = new List<string>();
        if (match.Mods != Mods.NoMod) activeMods.Add(match.Mods.ToString());
        if (match.Freemods) activeMods.Add("Freemod");
        if (activeMods.Count > 0) lines.Add($"Active mods: {string.Join(", ", activeMods)}");

        var occupied = match.Slots
            .Select((slot, i) => (slot, i))
            .Where(t => !t.slot.Empty)
            .ToList();
        lines.Add($"Players: {occupied.Count}");

        var hostSlotId = match.GetSlotId(match.HostId);
        foreach (var (slot, i) in occupied)
        {
            var tags = new List<string>();
            if (i == hostSlotId) tags.Add("Host");
            if (slot.Mods != Mods.NoMod) tags.Add(slot.Mods.ToString());
            var tagText = tags.Count > 0 ? $"[{string.Join(" / ", tags)}]" : "";

            var name = sessionRegistry.GetById(slot.PlayerId!.Value)?.Name ?? $"#{slot.PlayerId}";
            lines.Add($"Slot {i + 1}  {SlotStatusText(slot.Status)} {slot.PlayerId} {name,-16}{tagText}");
        }

        return string.Join('\n', lines);
    }

    private static string SlotStatusText(SlotStatus status)
    {
        return status switch
        {
            SlotStatus.NotReady => "Not Ready",
            SlotStatus.NoMap => "No Map",
            _ => status.ToString()
        };
    }

    private string? SetRoomLocked(MatchSession match, bool locked)
    {
        _matchControl.SetLocked(match, locked);
        return locked ? "Locked the match" : "Unlocked the match";
    }

    private (bool Success, string? Reply) SetSize(MatchSession match, IReadOnlyList<string> args)
    {
        if (args.Count < 1 || !int.TryParse(args[0], out var size))
            return (false, "Usage: !mp size <1-16>");

        size = Math.Clamp(size, 1, 16);
        _matchControl.SetSize(match, size);
        return (true, $"Changed match to size {size}");
    }

    private (bool Success, string? Reply) MoveSlot(MatchSession match, IReadOnlyList<string> args)
    {
        if (args.Count < 2 || !int.TryParse(args[^1], out var destSlotId))
            return (false, "Usage: !mp move <name> <slot 1-16>");

        destSlotId = Math.Clamp(destSlotId, 1, 16);

        var targetName = string.Join(' ', args.Take(args.Count - 1));
        var target = sessionRegistry.GetByName(targetName);
        if (target is null || target.Match != match) return (false, $"{targetName} is not in this match.");

        var result = _matchControl.MoveSlot(match, target, destSlotId - 1);
        return result switch
        {
            MatchControlService.MoveResult.DestinationNotOpen => (false, "Destination slot is not open."),
            MatchControlService.MoveResult.TargetNotInMatch => (false, $"{targetName} is not in this match."),
            _ => (true, $"Moved {target.Name} into slot {destSlotId}")
        };
    }

    private (bool Success, string? Reply) SetHost(MatchSession match, IReadOnlyList<string> args)
    {
        if (args.Count < 1) return (false, "Usage: !mp host <name>");

        var targetName = string.Join(' ', args);
        var target = sessionRegistry.GetByName(targetName);
        if (target is null || target.Match != match) return (false, $"{targetName} is not in this match.");

        _matchControl.SetHost(match, target);
        return (true, $"Changed match host to {target.Name}");
    }

    private string? ClearHost(MatchSession match)
    {
        _matchControl.ClearHost(match);
        return "Cleared match host";
    }

    private (bool Success, string? Reply) SetName(MatchSession match, IReadOnlyList<string> args)
    {
        if (args.Count < 1) return (false, "Usage: !mp name <text>");

        _matchControl.SetName(match, string.Join(' ', args));
        return (true, $"Room name updated to \"{match.Name}\"");
    }

    private string? SetPassword(MatchSession match, IReadOnlyList<string> args)
    {
        var password = args.Count == 0 ? "" : string.Join(' ', args);
        _matchControl.SetPassword(match, password);
        return args.Count == 0 ? "Removed the match password" : "Changed the match password";
    }

    private string? SetPrivate(MatchSession match, IReadOnlyList<string> args)
    {
        if (args.Count == 0)
            return $"This match is {(match.IsPrivate ? "private" : "not private")}.";

        if (args[0] is "0" or "1")
        {
            _matchControl.SetPrivate(match, args[0] == "1");
            return match.IsPrivate
                ? "The match is now private. It will be hidden from the lobby."
                : "The match is now public.";
        }

        return "Usage: !mp private [0|1]";
    }

    private (bool Success, string? Reply) Invite(PlayerSession sender, MatchSession match, IReadOnlyList<string> args)
    {
        if (args.Count < 1) return (false, "Usage: !mp invite <name>");

        var targetName = string.Join(' ', args);
        var target = sessionRegistry.GetByName(targetName);
        if (target is null) return (false, $"User not found: {targetName}");

        var result = _matchControl.Invite(sender, match, target);
        return result == MatchControlService.InviteResult.TargetAlreadyInRoom
            ? (false, "User is already in the room")
            : (true, $"Invited {target.Name} to the room");
    }

    private async Task<(bool Success, string? Reply)> AddRefereeAsync(PlayerSession sender, MatchSession match,
        IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        if (args.Count < 1) return (false, "Usage: !mp addref <name>");

        var targetName = string.Join(' ', args);
        var target = sessionRegistry.GetByName(targetName);
        if (target is null) return (false, $"User not found: {targetName}");

        await _matchControl.AddRefereeAsync(sender.Id, sender.Name, match, target, cancellationToken);
        return (true, $"Added {target.Name} to the match referees");
    }

    /// <summary>
    ///     Removing the last referee from a `!mp make`-created room disbands it immediately (see
    ///     <see cref="MatchSession.CreatedViaMakeCommand" />'s doc comment) — normal client-created
    ///     rooms are unaffected, they keep tearing down only once every slot empties.
    /// </summary>
    private async Task<(bool Success, string? Reply)> RemoveRefereeAsync(PlayerSession sender, MatchSession match,
        IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        if (args.Count < 1) return (false, "Usage: !mp removeref <name>");

        var targetName = string.Join(' ', args);
        var target = sessionRegistry.GetByName(targetName);
        if (target is null) return (false, $"User not found: {targetName}");

        var closed = await _matchControl.RemoveRefereeAsync(sender.Id, sender.Name, match, target, cancellationToken);
        return closed
            ? (true, $"Removed {target.Name} from the match referees. No referees remain — match closed")
            : (true, $"Removed {target.Name} from the match referees");
    }

    private string ListReferees(MatchSession match)
    {
        if (match.Referees.Count == 0) return "No referees";

        var names = match.Referees
            .Select(id => sessionRegistry.GetById(id)?.Name ?? $"#{id}")
            .ToList();
        return "Match referees:\n" + string.Join('\n', names);
    }

    private async Task<string?> BanListAsync(MatchSession match, CancellationToken cancellationToken)
    {
        if (match.BannedIds.Count == 0) return "No banned players";

        var names = new List<string>();
        foreach (var id in match.BannedIds)
        {
            var user = await userRepository.FetchByIdAsync(id, cancellationToken);
            names.Add(user?.Name ?? $"#{id}");
        }

        return "Match bans:\n" + string.Join('\n', names);
    }

    private (bool Success, string? Reply) SetTeam(MatchSession match, IReadOnlyList<string> args)
    {
        if (args.Count < 2) return (false, "Usage: !mp team <name> <red|blue>");

        var teamArg = args[^1].ToLowerInvariant();
        if (teamArg is not ("red" or "blue")) return (false, "Usage: !mp team <name> <red|blue>");

        var targetName = string.Join(' ', args.Take(args.Count - 1));
        var target = sessionRegistry.GetByName(targetName);
        if (target is null) return (false, $"{targetName} is not in this match.");

        var team = teamArg == "red" ? MatchTeam.Red : MatchTeam.Blue;
        var result = _matchControl.SetTeam(match, target, team);
        if (result == MatchControlService.TeamResult.TargetNotInMatch)
            return (false, $"{targetName} is not in this match.");

        var teamDisplay = char.ToUpperInvariant(teamArg[0]) + teamArg[1..];
        return (true, $"Moved {target.Name} to team {teamDisplay}");
    }

    private (bool Success, string? Reply) Set(MatchSession match, IReadOnlyList<string> args)
    {
        const string usage = "Usage: !mp set <teammode 0-3> [scoremode 0-3] [size 1-16]";

        if (args.Count < 1 || !TryParseTeamType(args[0], out var teamType)) return (false, usage);

        MatchWinCondition? winCondition = null;
        if (args.Count >= 2)
        {
            if (!TryParseWinCondition(args[1], out var parsed)) return (false, usage);
            winCondition = parsed;
        }

        int? size = null;
        if (args.Count >= 3)
        {
            if (!int.TryParse(args[2], out var parsedSize)) return (false, usage);
            size = Math.Clamp(parsedSize, 1, 16);
        }

        _matchControl.SetTeamTypeWinConditionAndSize(match, teamType, winCondition, size);
        return (true, $"Changed match settings to {match.TeamType}, {match.WinCondition}" +
                      (size is { } sz ? $", {sz} slots." : "."));
    }

    private static bool TryParseTeamType(string arg, out MatchTeamType teamType)
    {
        teamType = default;
        if (!int.TryParse(arg, out var value) || value is < 0 or > 3) return false;

        teamType = (MatchTeamType)value;
        return true;
    }

    private static bool TryParseWinCondition(string arg, out MatchWinCondition winCondition)
    {
        winCondition = default;
        if (!int.TryParse(arg, out var value) || value is < 0 or > 3) return false;

        winCondition = (MatchWinCondition)value;
        return true;
    }

    private async Task<(bool Success, string? Reply)> SetMapAsync(MatchSession match, IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (args.Count < 1 || !int.TryParse(args[0], out var beatmapId)) return (false, "Usage: !mp map <beatmap id>");

        var (result, bmap) = await _matchControl.SetMapAsync(match, beatmapId, cancellationToken);
        return result == MatchControlService.SetMapResult.BeatmapNotFound
            ? (false, $"No beatmap with id {beatmapId} found locally.")
            : (true, $"Changed beatmap to {bmap!.Mapset.Artist} - {bmap.Mapset.Title}");
    }

    /// <summary>
    ///     Ported to match real Bancho exactly: `!mp mods` is the ONLY mod-setting command — freemod is
    ///     just one of the values it accepts (`!mp mods Freemod`), not a separate `!mp freemods` toggle.
    ///     `None` clears the mods (and freemod, if on).
    /// </summary>
    private (bool Success, string? Reply) SetMods(MatchSession match, IReadOnlyList<string> args)
    {
        if (args.Count < 1) return (false, "Usage: !mp mods <mods>|Freemod|None");

        if (args.Any(a => a.Equals("Freemod", StringComparison.OrdinalIgnoreCase)))
        {
            _matchControl.SetMods(match, Mods.NoMod, true);
            return (true, "Enabled FreeMod");
        }

        var before = match.Mods;
        var wasFreemod = match.Freemods;

        var mods = Mods.NoMod;
        foreach (var token in args)
        {
            if (token.Equals("None", StringComparison.OrdinalIgnoreCase)) continue;
            mods |= ModsExtensions.FromModString(token);
        }

        _matchControl.SetMods(match, mods, false);
        return (true, DescribeModChange(before, mods, wasFreemod));
    }

    /// <summary>
    ///     Reports which mods this call turned on/off relative to the match's previous global mods,
    ///     plus the current freemod state — matches real Bancho's wording for the plain (non-`Freemod`)
    ///     case. Real Bancho's diff also covers mixing a mod list with the `Freemod` keyword in one
    ///     call; that combined case isn't reproduced here (see the `Freemod` branch above, which just
    ///     reports "Enabled FreeMod" instead).
    /// </summary>
    private static string DescribeModChange(Mods before, Mods after, bool wasFreemod)
    {
        var enabled = after & ~before;
        var disabled = before & ~after;

        var parts = new List<string>();
        if (enabled != Mods.NoMod) parts.Add($"Enabled {enabled}");
        if (disabled != Mods.NoMod) parts.Add($"Disabled {disabled}");
        if (wasFreemod) parts.Add("disabled FreeMod");

        return parts.Count > 0 ? string.Join(", ", parts) : "No mod changes";
    }

    private async Task<(bool Success, string? Reply)> StartAsync(MatchSession match, IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        int? countdownSeconds = args.Count > 0 && int.TryParse(args[0], out var seconds) && seconds > 0
            ? seconds
            : null;

        var result = await _matchControl.StartAsync(match, countdownSeconds, cancellationToken);
        return result switch
        {
            MatchControlService.StartResult.AlreadyInProgress => (false, "Match is already in progress."),
            MatchControlService.StartResult.CountdownQueued => (true, $"Match starts in {countdownSeconds} seconds"),
            MatchControlService.StartResult.Started => (true, "Match started"),
            _ => (false, "Match cannot start because the beatmap does not exist on the server.")
        };
    }

    private (bool Success, string? Reply) Timer(MatchSession match, IReadOnlyList<string> args)
    {
        var seconds = 30;
        if (args.Count > 0 && (!int.TryParse(args[0], out seconds) || seconds <= 0))
            return (false, "Usage: !mp timer [seconds]");

        _matchControl.Timer(match, seconds);
        return (true, $"Countdown started: {seconds} seconds");
    }

    private (bool Success, string? Reply) AbortTimer(MatchSession match)
    {
        var result = _matchControl.AbortTimer(match);
        return result == MatchControlService.AbortTimerResult.NoTimerRunning
            ? (false, "No countdown is running.")
            : (true, "Countdown aborted");
    }

    private async Task<(bool Success, string? Reply)> AbortAsync(MatchSession match,
        CancellationToken cancellationToken)
    {
        var result = await _matchControl.AbortAsync(match, cancellationToken);
        return result == MatchControlService.AbortResult.NotInProgress
            ? (false, "Match is not in progress.")
            : (true, "Aborted the match");
    }

    private async Task<(bool Success, string? Reply)> KickAsync(PlayerSession sender, MatchSession match,
        IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        if (args.Count < 1) return (false, "Usage: !mp kick <name>");

        var targetName = string.Join(' ', args);
        var target = sessionRegistry.GetByName(targetName);
        if (target is null || target.Match != match) return (false, $"{targetName} is not in this match.");

        await _matchControl.KickAsync(sender.Id, sender.Name, match, target, cancellationToken);
        return (true, $"Kicked {target.Name} from the match");
    }

    private async Task<(bool Success, string? Reply)> BanAsync(PlayerSession sender, MatchSession match,
        IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        if (args.Count < 1) return (false, "Usage: !mp ban <name>");

        var targetName = string.Join(' ', args);
        var target = sessionRegistry.GetByName(targetName);
        if (target is null || target.Match != match) return (false, $"{targetName} is not in this match.");

        await _matchControl.BanAsync(sender.Id, sender.Name, match, target, cancellationToken);
        return (true, $"Banned {target.Name} from the match");
    }

    private async Task<(bool Success, string? Reply)> UnbanAsync(MatchSession match, IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (args.Count < 1) return (false, "Usage: !mp unban <name>");

        var targetName = string.Join(' ', args);
        var targetUser = await userRepository.FetchByNameAsync(targetName, cancellationToken);
        if (targetUser is null) return (false, $"{targetName} is not registered.");

        var result = _matchControl.Unban(match, targetUser.Id);
        return result == MatchControlService.UnbanResult.NotBanned
            ? (false, $"{targetUser.Name} is not banned from this match.")
            : (true, $"Unbanned {targetUser.Name} from the match");
    }

    private async Task<string?> CloseAsync(PlayerSession sender, MatchSession match, CancellationToken cancellationToken)
    {
        await _matchControl.CloseAsync(sender.Id, sender.Name, match, cancellationToken);
        return "Closed the match";
    }

    /// <summary>
    ///     Forwards to <see cref="MatchControlService.ComputeAnnounceCheckpoints" /> — kept here too since
    ///     existing tests reference it as <c>MpCommandService.ComputeAnnounceCheckpoints</c>.
    /// </summary>
    public static IReadOnlyList<int> ComputeAnnounceCheckpoints(int totalSeconds)
    {
        return MatchControlService.ComputeAnnounceCheckpoints(totalSeconds);
    }

    /// <summary>One entry in the auto-generated `!mp help` listing — usage plus a one-line description.</summary>
    private readonly record struct CommandInfo(string Usage, string Description);
}
