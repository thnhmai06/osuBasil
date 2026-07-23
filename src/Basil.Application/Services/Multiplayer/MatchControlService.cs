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
    }

    public void ClearHost(MatchSession match)
    {
        match.HostId = 0;
        matchMembership.EnqueueState(match);
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
    }

    /// <summary>
    ///     Removing the last referee from a `!mp make`-created room disbands it immediately (see
    ///     <see cref="MatchSession.CreatedViaMakeCommand" />'s doc comment) — normal client-created rooms
    ///     are unaffected. Returns whether the match was closed as a result.
    /// </summary>
    public async Task<bool> RemoveRefereeAsync(int? actorId, string? actorName, MatchSession match,
        PlayerSession target, CancellationToken cancellationToken = default)
    {
        match.RemoveReferee(target.Id);

        await matchPersistence.CreateEventAsync(new MatchEventRow(
            match.DbId, (int)MatchEventType.RefRemoved,
            actorId, actorName, target.Id, target.Name,
            DateTimeOffset.UtcNow.UtcDateTime, null), cancellationToken);

        if (match is not { CreatedViaMakeCommand: true, Referees.Count: 0 }) return false;

        await matchMembership.CloseAsync(match, cancellationToken: cancellationToken);
        return true;
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
            remaining = checkpoint;
        }

        if (!await DelayAsync(remaining, token)) return;

        await match.Lock.WaitAsync(token);
        try
        {
            if (token.IsCancellationRequested) return;
            match.PendingTimer = null;
            match.PendingTimerIsAutoStart = false;

            if (autoStart)
            {
                var started = match.InProgress || await matchMembership.StartAsync(match, token);
                if (started) Announce(match, "Good luck, have fun!");
            }
            else
            {
                Announce(match, "Countdown finished");
            }
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
        return UnbanResult.Ok;
    }

    public async Task CloseAsync(int? actorId, string? actorName, MatchSession match,
        CancellationToken cancellationToken = default)
    {
        await matchMembership.CloseAsync(match, actorId, actorName, cancellationToken);
    }
}
