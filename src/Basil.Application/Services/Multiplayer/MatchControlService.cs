using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Services.Bot;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Protocol.Multiplayer;
using Basil.Protocol.Packets;

namespace Basil.Application.Services.Multiplayer;

/// <summary>
///     The room-adjustment mutation logic every `!mp` subcommand (see <c>MpCommandService</c>) and the
///     matching HTTP write route (see the <c>api.</c> host's <c>/match/{id}/settings</c> and
///     <c>/match/{id}/{action}</c>) both need — extracted so the two surfaces call the identical
///     state-mutation/broadcast code instead of duplicating it. Callers own everything surface-specific:
///     resolving a target player (chat resolves by name via <see cref="IPlayerSessionRegistry.GetByName" />,
///     HTTP resolves by numeric id via <see cref="IPlayerSessionRegistry.GetById" />), parsing/validating
///     raw input, and formatting a reply/response from the result. Every method here assumes the caller
///     already holds the match's <see cref="MatchSession.Lock" /> for the whole read-mutate-broadcast
///     sequence, exactly like every packet handler and <c>MpCommandService</c>'s own <c>RunLockedAsync</c>
///     wrapper already do — this class never acquires the lock itself.
/// </summary>
public sealed class MatchControlService(
    MatchMembershipService matchMembership,
    IMatchPersistenceRepository matchPersistence,
    IMapRepository mapRepository,
    IPlayerSessionRegistry sessionRegistry)
{
    public const int MaxMatchNameLength = 50;

    /// <summary>
    ///     Ported from `!mp lock`/`!mp unlock` verbatim — note no <c>EnqueueState</c> broadcast happens
    ///     here, matching the pre-existing chat behavior (not something this extraction changes).
    /// </summary>
    public void SetLocked(MatchSession match, bool locked)
    {
        match.IsLocked = locked;
    }

    public void SetPrivate(MatchSession match, bool isPrivate)
    {
        match.IsPrivate = isPrivate;
        matchMembership.EnqueueState(match);
    }

    public void SetSize(MatchSession match, int size)
    {
        size = Math.Clamp(size, 1, 16);
        ApplySize(match, size);
        matchMembership.EnqueueState(match);
        matchMembership.CancelQueuedAutoStart(match);
    }

    public static void ApplySize(MatchSession match, int size)
    {
        for (var i = 0; i < 16; i++)
        {
            var slot = match.Slots[i];
            if (!slot.Empty) continue;

            if (i >= size && slot.Status == SlotStatus.Open) slot.Status = SlotStatus.Locked;
            else if (i < size && slot.Status == SlotStatus.Locked) slot.Status = SlotStatus.Open;
        }
    }

    public enum MoveResult
    {
        Ok,
        DestinationNotOpen,
        TargetNotInMatch
    }

    /// <summary><paramref name="destSlotIndex" /> is 0-based; callers convert from their own 1-based input.</summary>
    public MoveResult MoveSlot(MatchSession match, PlayerSession target, int destSlotIndex)
    {
        var destSlot = match.Slots[destSlotIndex];
        if (destSlot.Status != SlotStatus.Open) return MoveResult.DestinationNotOpen;

        var sourceSlot = match.GetSlot(target.Id);
        if (sourceSlot is null) return MoveResult.TargetNotInMatch;

        destSlot.CopyFrom(sourceSlot);
        sourceSlot.Reset();
        matchMembership.EnqueueState(match);
        return MoveResult.Ok;
    }

    public void SetHost(MatchSession match, PlayerSession target)
    {
        var prevHostId = match.HostId;
        match.HostId = target.Id;
        target.Enqueue(ServerPacketWriter.MatchTransferHost());
        matchMembership.EnqueueState(match);

        var prevHostName = sessionRegistry.GetById(prevHostId)?.Name;
        _ = matchPersistence.CreateEventAsync(new MatchEventRow(
            match.DbId, (int)MatchEventType.HostGranted,
            prevHostId, prevHostName, target.Id, target.Name,
            DateTimeOffset.UtcNow.UtcDateTime, null));

        matchMembership.PublishHost(match);
    }

    public void ClearHost(MatchSession match)
    {
        match.HostId = 0;
        matchMembership.EnqueueState(match);
        matchMembership.PublishHost(match);
    }

    public void SetName(MatchSession match, string name)
    {
        if (name.Length > MaxMatchNameLength) name = name[..MaxMatchNameLength];

        match.Name = name;
        matchMembership.EnqueueState(match);
    }

    /// <summary>Empty string clears the password, matching `!mp password` with no argument.</summary>
    public void SetPassword(MatchSession match, string password)
    {
        match.Password = password;
        matchMembership.EnqueueState(match);
    }

    public enum InviteResult
    {
        Ok,
        TargetAlreadyInRoom
    }

    public InviteResult Invite(PlayerSession sender, MatchSession match, PlayerSession target)
    {
        if (target.Match == match) return InviteResult.TargetAlreadyInRoom;

        match.AddInvite(target.Id);
        target.Enqueue(ServerPacketWriter.MatchInvite(sender.Id, sender.Name, match.Embed, target.Name));
        return InviteResult.Ok;
    }

    public async Task AddRefereeAsync(int? actorId, string? actorName, MatchSession match, PlayerSession target,
        CancellationToken cancellationToken = default)
    {
        match.AddReferee(target.Id);

        await matchPersistence.CreateEventAsync(new MatchEventRow(
            match.DbId, (int)MatchEventType.RefAdded,
            actorId, actorName, target.Id, target.Name,
            DateTimeOffset.UtcNow.UtcDateTime, null), cancellationToken);

        matchMembership.PublishRefs(match);
    }

    public enum SetRefereesResult
    {
        Ok,
        WouldLeaveEmpty
    }

    /// <summary>PUT — full replace. 409 (<see cref="SetRefereesResult.WouldLeaveEmpty" />) if it would end up empty.</summary>
    public async Task<SetRefereesResult> SetRefereesAsync(MatchSession match, IReadOnlyCollection<PlayerSession> targets,
        CancellationToken cancellationToken = default)
    {
        if (targets.Count == 0) return SetRefereesResult.WouldLeaveEmpty;

        var newIds = targets.Select(t => t.Id).ToHashSet();
        var toRemove = match.Referees.Where(id => !newIds.Contains(id)).ToList();
        var toAdd = targets.Where(t => !match.Referees.Contains(t.Id)).ToList();

        foreach (var id in toRemove)
        {
            var removedName = sessionRegistry.GetById(id)?.Name;
            match.RemoveReferee(id);
            await matchPersistence.CreateEventAsync(new MatchEventRow(
                match.DbId, (int)MatchEventType.RefRemoved,
                null, null, id, removedName, DateTimeOffset.UtcNow.UtcDateTime, null), cancellationToken);
        }

        foreach (var target in toAdd)
        {
            match.AddReferee(target.Id);
            await matchPersistence.CreateEventAsync(new MatchEventRow(
                match.DbId, (int)MatchEventType.RefAdded,
                null, null, target.Id, target.Name, DateTimeOffset.UtcNow.UtcDateTime, null), cancellationToken);
        }

        matchMembership.PublishRefs(match);
        return SetRefereesResult.Ok;
    }

    /// <summary>PATCH — add a batch. Never triggers the empty guard (it only ever adds referees).</summary>
    public async Task AddRefereesAsync(MatchSession match, IReadOnlyCollection<PlayerSession> targets,
        CancellationToken cancellationToken = default)
    {
        foreach (var target in targets)
        {
            if (match.Referees.Contains(target.Id)) continue;

            match.AddReferee(target.Id);
            await matchPersistence.CreateEventAsync(new MatchEventRow(
                match.DbId, (int)MatchEventType.RefAdded,
                null, null, target.Id, target.Name, DateTimeOffset.UtcNow.UtcDateTime, null), cancellationToken);
        }

        matchMembership.PublishRefs(match);
    }

    public enum RemoveRefereeResult
    {
        Ok,
        NotAReferee,
        WouldLeaveEmpty
    }

    /// <summary>
    ///     Replaces the old bool-returning "auto-close a `!mp make` room when its last referee is
    ///     removed" behavior with a guard that blocks removing the last referee at all — the auto-close
    ///     branch is now unreachable dead code, since a match can never actually reach zero referees
    ///     through this path anymore.
    /// </summary>
    public async Task<RemoveRefereeResult> RemoveOneRefereeAsync(int? actorId, string? actorName, MatchSession match,
        PlayerSession target, CancellationToken cancellationToken = default)
    {
        if (!match.Referees.Contains(target.Id)) return RemoveRefereeResult.NotAReferee;
        if (match.Referees.Count == 1) return RemoveRefereeResult.WouldLeaveEmpty;

        match.RemoveReferee(target.Id);

        await matchPersistence.CreateEventAsync(new MatchEventRow(
            match.DbId, (int)MatchEventType.RefRemoved,
            actorId, actorName, target.Id, target.Name,
            DateTimeOffset.UtcNow.UtcDateTime, null), cancellationToken);

        matchMembership.PublishRefs(match);
        return RemoveRefereeResult.Ok;
    }

    public enum TeamResult
    {
        Ok,
        TargetNotInMatch
    }

    public TeamResult SetTeam(MatchSession match, PlayerSession target, MatchTeam team)
    {
        var slot = match.GetSlot(target.Id);
        if (slot is null) return TeamResult.TargetNotInMatch;

        slot.Team = team;
        matchMembership.EnqueueState(match, false);
        matchMembership.CancelQueuedAutoStart(match);
        return TeamResult.Ok;
    }

    public static void ApplyTeamType(MatchSession match, MatchTeamType newType)
    {
        if (match.TeamType == newType) return;

        var newTeam = newType is MatchTeamType.HeadToHead or MatchTeamType.TagCoop
            ? MatchTeam.Neutral
            : MatchTeam.Red;

        foreach (var slot in match.Slots)
            if (slot.PlayerId is not null)
                slot.Team = newTeam;

        match.TeamType = newType;
    }

    public void SetTeamTypeWinConditionAndSize(MatchSession match, MatchTeamType teamType,
        MatchWinCondition? winCondition, int? size)
    {
        ApplyTeamType(match, teamType);
        if (winCondition is { } wc) match.WinCondition = wc;
        if (size is { } s) ApplySize(match, s);

        matchMembership.EnqueueState(match);
        matchMembership.CancelQueuedAutoStart(match);
    }

    public enum SetMapResult
    {
        Ok,
        BeatmapNotFound
    }

    /// <summary>Returns the resolved beatmap alongside the result so callers don't need a second lookup.</summary>
    public async Task<(SetMapResult Result, Beatmap? Beatmap)> SetMapAsync(MatchSession match, int beatmapId,
        CancellationToken cancellationToken = default)
    {
        var bmap = await mapRepository.FetchOneAsync(beatmapId, cancellationToken: cancellationToken);
        if (bmap is null) return (SetMapResult.BeatmapNotFound, null);

        match.UnreadyPlayers();
        match.MapId = bmap.Id;
        match.MapMd5 = bmap.Md5;
        match.MapName = bmap.FullName;
        match.Mode = bmap.Difficulty.Mode;
        matchMembership.EnqueueState(match);
        matchMembership.CancelQueuedAutoStart(match);
        return (SetMapResult.Ok, bmap);
    }

    /// <summary>
    ///     Ported to match real Bancho exactly: mod-setting is the ONLY place freemod toggles — it's
    ///     just one of the values a caller can pass (<paramref name="enableFreemod" />), not a separate
    ///     command. Passing <paramref name="enableFreemod" /> ignores <paramref name="mods" />.
    /// </summary>
    public void SetMods(MatchSession match, Mods mods, bool enableFreemod)
    {
        if (enableFreemod)
        {
            EnableFreemods(match);
            matchMembership.EnqueueState(match);
            return;
        }

        if (match.Freemods) DisableFreemods(match);

        match.Mods = mods;
        matchMembership.EnqueueState(match);
    }

    private static void EnableFreemods(MatchSession match)
    {
        if (match.Freemods) return;

        match.Freemods = true;
        foreach (var slot in match.Slots)
            if (slot.PlayerId is not null)
                slot.Mods = match.Mods & ~ModsExtensions.SpeedChangingMods;

        match.Mods &= ModsExtensions.SpeedChangingMods;
    }

    private static void DisableFreemods(MatchSession match)
    {
        var hostSlot = match.GetHostSlot();
        match.Freemods = false;
        match.Mods &= ModsExtensions.SpeedChangingMods;
        if (hostSlot is not null) match.Mods |= hostSlot.Mods;

        foreach (var slot in match.Slots)
            if (slot.PlayerId is not null)
                slot.Mods = Mods.NoMod;
    }

    public enum StartResult
    {
        AlreadyInProgress,
        Started,
        CountdownQueued,
        BeatmapMissing
    }

    /// <summary><paramref name="countdownSeconds" /> null/non-positive starts immediately instead of queuing.</summary>
    public async Task<StartResult> StartAsync(MatchSession match, int? countdownSeconds,
        CancellationToken cancellationToken = default)
    {
        if (match.InProgress) return StartResult.AlreadyInProgress;

        if (countdownSeconds is > 0)
        {
            BeginCountdown(match, countdownSeconds.Value, true);
            return StartResult.CountdownQueued;
        }

        var started = await matchMembership.StartAsync(match, cancellationToken);
        return started ? StartResult.Started : StartResult.BeatmapMissing;
    }

    /// <summary>Backs `!mp timer` — a countdown that never auto-starts the match when it finishes.</summary>
    public void Timer(MatchSession match, int seconds)
    {
        BeginCountdown(match, seconds, false);
    }

    private static readonly int[] TimerCheckpoints = [60, 30, 10, 5, 4, 3, 2, 1];
    private const int PeriodicReminderIntervalSeconds = 60;
    private const int NearTotalIgnoreWindowSeconds = 5;

    /// <summary>
    ///     Fixed marks plus an extra reminder every 60s for long countdowns (e.g. a 5-minute timer also
    ///     announces at 240/180/120). A mark that's a multiple of 60 (whether from the fixed list's own
    ///     "60" or a periodic one) is dropped if it's within <see cref="NearTotalIgnoreWindowSeconds" />
    ///     of <paramref name="totalSeconds" /> — otherwise it'd fire almost immediately after "Queued...",
    ///     which is redundant. The sub-60 marks (30/10/5/4/3/2/1) are exempt from that check — they're
    ///     meant to fire close together as the final countdown ticks down.
    /// </summary>
    public static IReadOnlyList<int> ComputeAnnounceCheckpoints(int totalSeconds)
    {
        var periodic = Enumerable.Range(1, int.MaxValue)
            .Select(k => k * PeriodicReminderIntervalSeconds)
            .TakeWhile(c => c < totalSeconds);

        return TimerCheckpoints
            .Concat(periodic)
            .Where(c => c < totalSeconds)
            .Distinct()
            .Where(c => c % PeriodicReminderIntervalSeconds != 0 || totalSeconds - c > NearTotalIgnoreWindowSeconds)
            .OrderByDescending(c => c)
            .ToList();
    }

    /// <summary>
    ///     Kicks off a fire-and-forget countdown, cancelling any timer already pending on this match.
    ///     The loop itself (<see cref="CountdownLoopAsync" />) only holds <see cref="MatchSession.Lock" />
    ///     briefly at its final tick — never across a `Task.Delay` — matching this codebase's rule
    ///     against holding the lock across an unrelated await.
    /// </summary>
    private void BeginCountdown(MatchSession match, int totalSeconds, bool autoStart)
    {
        match.PendingTimer?.Cancel();
        var cts = new CancellationTokenSource();
        match.PendingTimer = cts;
        match.PendingTimerIsAutoStart = autoStart;
        match.TimerStartedAt = DateTimeOffset.UtcNow;
        match.TimerTotalSeconds = totalSeconds;
        matchMembership.PublishTimer(match);

        _ = CountdownLoopAsync(match, totalSeconds, autoStart, cts);
    }

    private async Task CountdownLoopAsync(MatchSession match, int totalSeconds, bool autoStart,
        CancellationTokenSource cts)
    {
        var token = cts.Token;
        Announce(match, $"Queued the match to start in {totalSeconds} seconds");

        var remaining = totalSeconds;
        foreach (var checkpoint in ComputeAnnounceCheckpoints(totalSeconds))
        {
            if (!await DelayAsync(remaining - checkpoint, token)) return;

            Announce(match, $"Match starts in {checkpoint} seconds");
            matchMembership.PublishTimer(match);
            remaining = checkpoint;
        }

        if (!await DelayAsync(remaining, token)) return;

        await match.Lock.WaitAsync(token);
        try
        {
            if (token.IsCancellationRequested) return;
            match.PendingTimer = null;
            match.PendingTimerIsAutoStart = false;
            match.TimerStartedAt = null;
            match.TimerTotalSeconds = null;

            if (autoStart)
            {
                var started = match.InProgress || await matchMembership.StartAsync(match, token);
                if (started) Announce(match, "Good luck, have fun!");
            }
            else
            {
                Announce(match, "Countdown finished");
            }

            matchMembership.PublishTimer(match);
        }
        finally
        {
            match.Lock.Release();
        }
    }

    /// <summary>Returns false (caller should stop) if cancelled during the delay.</summary>
    private static async Task<bool> DelayAsync(int seconds, CancellationToken token)
    {
        if (seconds <= 0) return !token.IsCancellationRequested;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds), token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private void Announce(MatchSession match, string text)
    {
        var bot = sessionRegistry.GetById(BotBootstrapService.BotId);
        if (bot is null) return;

        matchMembership.EnqueueChat(match, bot.Name, bot.Id, text);
    }

    public enum AbortTimerResult
    {
        Ok,
        NoTimerRunning
    }

    public AbortTimerResult AbortTimer(MatchSession match)
    {
        if (match.PendingTimer is null) return AbortTimerResult.NoTimerRunning;

        match.PendingTimer.Cancel();
        match.PendingTimer = null;
        match.PendingTimerIsAutoStart = false;
        match.TimerStartedAt = null;
        match.TimerTotalSeconds = null;
        matchMembership.PublishTimer(match);
        return AbortTimerResult.Ok;
    }

    public enum AbortResult
    {
        Ok,
        NotInProgress
    }

    public async Task<AbortResult> AbortAsync(MatchSession match, CancellationToken cancellationToken = default)
    {
        if (!match.InProgress) return AbortResult.NotInProgress;

        match.UnreadyPlayers(SlotStatus.Playing);
        match.ResetPlayersLoadedStatus();
        match.InProgress = false;

        if (match.CurrentRoundId is { } roundId)
        {
            await matchPersistence.SetRoundEndedAsync(roundId, DateTimeOffset.UtcNow.UtcDateTime, true, cancellationToken);
            match.CurrentRoundId = null;
        }

        matchMembership.Enqueue(match, ServerPacketWriter.MatchAbort(), false);
        matchMembership.EnqueueState(match);
        return AbortResult.Ok;
    }

    public enum KickResult
    {
        Ok,
        TargetNotInMatch
    }

    public async Task<KickResult> KickAsync(int? actorId, string? actorName, MatchSession match, PlayerSession target,
        CancellationToken cancellationToken = default)
    {
        if (target.Match != match) return KickResult.TargetNotInMatch;

        matchMembership.Leave(target, match);
        target.Enqueue(ServerPacketWriter.MatchJoinFail());

        await matchPersistence.CreateEventAsync(new MatchEventRow(
            match.DbId, (int)MatchEventType.Kicked,
            actorId, actorName, target.Id, target.Name,
            DateTimeOffset.UtcNow.UtcDateTime, "Kicked"), cancellationToken);

        return KickResult.Ok;
    }

    public async Task<KickResult> BanAsync(int? actorId, string? actorName, MatchSession match, PlayerSession target,
        CancellationToken cancellationToken = default)
    {
        if (target.Match != match) return KickResult.TargetNotInMatch;

        match.AddBan(target.Id);
        matchMembership.Leave(target, match);
        target.Enqueue(ServerPacketWriter.MatchJoinFail());

        await matchPersistence.CreateEventAsync(new MatchEventRow(
            match.DbId, (int)MatchEventType.Kicked,
            actorId, actorName, target.Id, target.Name,
            DateTimeOffset.UtcNow.UtcDateTime, "Banned"), cancellationToken);

        matchMembership.PublishBans(match);
        return KickResult.Ok;
    }

    public enum UnbanResult
    {
        Ok,
        NotBanned
    }

    public UnbanResult Unban(MatchSession match, int targetUserId)
    {
        if (!match.BannedIds.Contains(targetUserId)) return UnbanResult.NotBanned;

        match.RemoveBan(targetUserId);
        matchMembership.PublishBans(match);
        return UnbanResult.Ok;
    }

    /// <summary>PUT — full replace of the ban list. No empty guard: banning down to zero is fine.</summary>
    public void SetBans(MatchSession match, IReadOnlyCollection<int> userIds)
    {
        var newIds = userIds.ToHashSet();
        var toRemove = match.BannedIds.Where(id => !newIds.Contains(id)).ToList();
        var toAdd = newIds.Where(id => !match.BannedIds.Contains(id)).ToList();

        foreach (var id in toRemove) match.RemoveBan(id);
        foreach (var id in toAdd) AddBanAndKickIfSeated(match, id);

        matchMembership.PublishBans(match);
    }

    /// <summary>PATCH — add a batch of bans, each newly-banned id who is currently seated is also kicked.</summary>
    public void AddBans(MatchSession match, IReadOnlyCollection<int> userIds)
    {
        foreach (var id in userIds)
        {
            if (match.BannedIds.Contains(id)) continue;
            AddBanAndKickIfSeated(match, id);
        }

        matchMembership.PublishBans(match);
    }

    private void AddBanAndKickIfSeated(MatchSession match, int userId)
    {
        match.AddBan(userId);

        var seated = sessionRegistry.GetById(userId);
        if (seated is null || seated.Match != match) return;

        matchMembership.Leave(seated, match);
        seated.Enqueue(ServerPacketWriter.MatchJoinFail());
    }

    public enum ForceInviteResult
    {
        Ok,
        NoFreeSlot,
        TargetBanned,
        TargetInAnotherMatch
    }

    /// <summary>
    ///     `force: true` on `POST /matches/{matchId}/invite` — bypasses password/private/locked gating
    ///     and seats the target directly, but a banned target is still rejected (the one gate force does
    ///     not cross).
    /// </summary>
    public ForceInviteResult ForceInvite(MatchSession match, PlayerSession target)
    {
        if (match.BannedIds.Contains(target.Id)) return ForceInviteResult.TargetBanned;
        if (target.Match == match) return ForceInviteResult.Ok;
        if (target.Match is not null) return ForceInviteResult.TargetInAnotherMatch;

        return matchMembership.ForceJoin(target, match) ? ForceInviteResult.Ok : ForceInviteResult.NoFreeSlot;
    }

    /// <summary>One entry in a `PUT`/`PATCH /matches/{matchId}/slots` request, keyed by slot index (0-based).</summary>
    public sealed record SlotPatchEntry(int? UserId, string? Team, bool? Locked);

    public enum SetSlotsResult
    {
        Ok,
        PlayerCountMismatch,
        UnknownUserId,
        SlotOccupiedAndLocked
    }

    /// <summary>
    ///     Reassigns/re-teams/locks slots in one atomic pass. Every <see cref="SlotPatchEntry.UserId" />
    ///     referenced anywhere in <paramref name="entries" /> must already occupy some slot in this
    ///     match (<see cref="SetSlotsResult.UnknownUserId" /> otherwise) — this never seats a new
    ///     player, only rearranges existing occupants. <paramref name="isFullReplace" /> (PUT) also
    ///     requires the referenced user ids to exactly match the match's current full occupant set
    ///     (<see cref="SetSlotsResult.PlayerCountMismatch" /> otherwise); PATCH only touches the slots
    ///     actually given. A <see cref="SlotPatchEntry.Team" /> value other than the literal strings
    ///     `"Red"`/`"Blue"` is a no-op — the destination slot's existing team is preserved, never reset
    ///     to neutral, and never inherited from the moving player's previous slot.
    /// </summary>
    public Task<SetSlotsResult> SetSlotsAsync(MatchSession match, IReadOnlyDictionary<int, SlotPatchEntry> entries,
        bool isFullReplace, CancellationToken cancellationToken = default)
    {
        foreach (var entry in entries.Values)
            if (entry.UserId is not null && entry.Locked == true)
                return Task.FromResult(SetSlotsResult.SlotOccupiedAndLocked);

        var currentOccupantIds = match.Slots
            .Where(s => s.PlayerId is not null)
            .Select(s => s.PlayerId!.Value)
            .ToHashSet();

        var referencedUserIds = entries.Values
            .Where(e => e.UserId is not null)
            .Select(e => e.UserId!.Value)
            .ToList();

        foreach (var uid in referencedUserIds)
            if (!currentOccupantIds.Contains(uid))
                return Task.FromResult(SetSlotsResult.UnknownUserId);

        if (isFullReplace)
        {
            var referencedSet = referencedUserIds.ToHashSet();
            if (referencedSet.Count != currentOccupantIds.Count || !referencedSet.SetEquals(currentOccupantIds))
                return Task.FromResult(SetSlotsResult.PlayerCountMismatch);
        }

        // Snapshot every slot's pre-mutation state so a swap (A<->B) can look up each player's
        // origin slot without being affected by the other entry's own mutation.
        var original = match.Slots.Select(s => (s.PlayerId, s.Status, s.Team, s.Mods)).ToArray();
        var destinationSlots = entries.Where(kv => kv.Value.UserId is not null).Select(kv => kv.Key).ToHashSet();

        // Vacate the previous slot of every moved player, unless that slot is itself a destination
        // in this same payload (a direct swap doesn't need clearing — it gets overwritten below).
        foreach (var (slotIndex, entry) in entries)
        {
            if (entry.UserId is not { } uid) continue;

            var oldIndex = Array.FindIndex(original, o => o.PlayerId == uid);
            if (oldIndex >= 0 && oldIndex != slotIndex && !destinationSlots.Contains(oldIndex))
                match.Slots[oldIndex].Reset();
        }

        foreach (var (slotIndex, entry) in entries)
        {
            var slot = match.Slots[slotIndex];

            if (entry.UserId is { } uid)
            {
                var oldIndex = Array.FindIndex(original, o => o.PlayerId == uid);
                var source = original[oldIndex];
                slot.PlayerId = uid;
                slot.Status = source.Status;
                slot.Mods = source.Mods;
            }

            if (entry.Team is "Red" or "Blue")
                slot.Team = entry.Team == "Red" ? MatchTeam.Red : MatchTeam.Blue;

            if (entry.Locked is { } locked && slot.PlayerId is null)
                slot.Status = locked ? SlotStatus.Locked : SlotStatus.Open;
        }

        matchMembership.PublishSlots(match);
        return Task.FromResult(SetSlotsResult.Ok);
    }

    public async Task CloseAsync(int? actorId, string? actorName, MatchSession match,
        CancellationToken cancellationToken = default)
    {
        await matchMembership.CloseAsync(match, actorId, actorName, cancellationToken);
    }
}
