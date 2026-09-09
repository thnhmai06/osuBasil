using Basil.Server.Features.Irc;
using Basil.Server.Shared.Eventing;
using Basil.Server.Features.Beatmaps;
using Basil.Server.Features.Multiplayer;
using Basil.Server.Features.Users;
using Basil.Server.Features.Bot;
using Basil.Server.Shared.Sessions;
using Basil.Server.Features.Chat;
using Basil.Protocol.Irc;
using Basil.Protocol.Packets;

namespace Basil.Server.Features.Multiplayer;

/// <summary>
///     Broadcasts a match's packets, chat announcements, and live snapshot channels to its chat
///     channel, the lobby, referees, and every SSE subscriber.
/// </summary>
/// <remarks>
///     The registered <see cref="IMatchMutationPublisher" /> for every match (see
///     <see cref="MatchSession.MutationPublisher" />): <see cref="MatchLifecycle" /> assigns this
///     instance to every match it creates, so a <see cref="MatchMutationScope" /> completing a
///     mutation always routes its publish requests here.
/// </remarks>
public sealed class MatchBroadcast(
	IChannelRegistry channelRegistry,
	ChannelMembershipService channelMembership,
	ISessionRegistry<GameSession> gameRegistry,
	ISessionRegistry<IrcSession> ircRegistry,
	IMatchLiveEvents eventBus,
	IBeatmapRepository beatmapRepo,
	IUserRepository userRepo) : IMatchMutationPublisher
{
	/// <inheritdoc />
	Task IMatchMutationPublisher.PublishStateAsync(MatchSession match, long version, bool lobby,
		CancellationToken cancellationToken) => EnqueueStateAsync(match, version, lobby, cancellationToken);

	/// <summary>Broadcasts a raw packet to the match channel and, for public rooms, the non-empty lobby.</summary>
	/// <param name="match">The match whose channel to broadcast into.</param>
	/// <param name="data">The serialized packet bytes.</param>
	/// <param name="lobby"><see langword="true" /> to also broadcast to the lobby; otherwise, <see langword="false" />.</param>
	/// <param name="immune">User ids to exclude from the broadcast, or <see langword="null" /> for none.</param>
	public void Enqueue(MatchSession match, byte[] data, bool lobby = true, IReadOnlyCollection<int>? immune = null)
	{
		var channel = channelRegistry.GetByName(match.ChatChannelName);
		if (channel is not null) channelMembership.BroadcastToMembers(channel, data, immune);

		if (!match.IsPrivate) BroadcastToNonEmptyLobby(data, lobby);
	}

	/// <summary>Broadcasts a chat message into the match channel.</summary>
	/// <param name="match">The match whose channel to broadcast into.</param>
	/// <param name="senderName">The message sender's name.</param>
	/// <param name="senderId">The message sender's id.</param>
	/// <param name="text">The message text.</param>
	public void EnqueueChat(MatchSession match, string senderName, int senderId, string text)
	{
		var channel = channelRegistry.GetByName(match.ChatChannelName);
		if (channel is null) return;

		channelMembership.BroadcastPrivmsg(channel,
			IrcMessageWriter.Privmsg(senderName, senderId, channel.Name, text));
	}

	private static readonly KeyValuePair<string, object?> PacketStreamTag = new("stream", "packet");

	/// <summary>Broadcasts the match state to the channel and lobby and republishes every live SSE snapshot channel.</summary>
	/// <remarks>
	///     Sends the <c>UpdateMatch</c> packet to the match channel and, for public rooms, the lobby,
	///     then rebuilds and publishes the main, settings, per-slot, and whole-arrangement slots
	///     snapshots through <see cref="IMatchLiveEvents" />. This is the single call path every
	///     slot-mutating operation (packet-driven or HTTP-driven) routes through, so <c>slot</c> and
	///     <c>slots</c> always fire together (ADR-004) — no separate path publishes one without the
	///     other. A channel whose <see cref="StateStream{T}.Publish" /> found nothing changed is
	///     skipped rather than emitting a no-op patch.
	/// </remarks>
	/// <remarks>
	///     Runs entirely without holding <see cref="MatchSession.Lock" /> — every build and broadcast
	///     here is gated by <paramref name="version" /> instead, so a call superseded by a newer
	///     mutation's is dropped rather than reverting live state to something stale. A
	///     <see cref="MatchMutationScope" /> allocates <paramref name="version" /> once it has
	///     released the lock, so the value always reflects a mutation that has already completed.
	/// </remarks>
	/// <param name="match">The match whose state to broadcast.</param>
	/// <param name="version">This call's state version, allocated by the calling <see cref="MatchMutationScope" />.</param>
	/// <param name="lobby"><see langword="true" /> to also broadcast to the lobby; otherwise, <see langword="false" />.</param>
	/// <param name="cancellationToken">A token that cancels the snapshot builds.</param>
	public async Task EnqueueStateAsync(MatchSession match, long version, bool lobby = true,
		CancellationToken cancellationToken = default)
	{
		if (match.PacketBroadcastGate.TryAdvance(version))
		{
			var channel = channelRegistry.GetByName(match.ChatChannelName);
			if (channel is not null)
				channelMembership.BroadcastToMembers(channel,
					ServerPacketWriter.UpdateMatch(match.ToPacket()));

			if (!match.IsPrivate)
				BroadcastToNonEmptyLobby(ServerPacketWriter.UpdateMatch(match.ToPacket(), false), lobby);
		}
		else
		{
			EventingMetrics.StalePublishDropped.Add(1, PacketStreamTag);
		}

		var mainSnapshot = await MatchLiveSnapshotBuilder.BuildMain(
			match, gameRegistry, ircRegistry, userRepo, beatmapRepo, cancellationToken);
		if (match.MainSnapshot.Publish(mainSnapshot, version) is { } mainDelta)
			eventBus.PublishMain(match.DbId, mainDelta);

		var settings = await MatchLiveSnapshotBuilder.BuildSettings(
			match, gameRegistry, ircRegistry, userRepo, beatmapRepo, cancellationToken);
		if (match.SettingsSnapshot.Publish(settings, version) is { } settingsDelta)
			eventBus.PublishSettings(match.DbId, settingsDelta);

		for (var i = 0; i < match.SlotSnapshots.Count; i++)
			if (match.SlotSnapshots[i].Publish(mainSnapshot.Slots[i], version) is { } slotDelta)
				eventBus.PublishSlot(match.DbId, i, slotDelta);

		// Reuses mainSnapshot.Slots (already resolved above) instead of a second occupant-lookup
		// pass — MatchSlotsView wraps the exact same per-slot view list BuildSlots itself produces.
		if (match.SlotsSnapshot.Publish(new MatchSlotsView(mainSnapshot.Slots), version) is { } slotsDelta)
			eventBus.PublishSlots(match.DbId, slotsDelta);
	}

	/// <summary>Rebuilds and republishes the host snapshot channel.</summary>
	/// <remarks>Runs without holding <see cref="MatchSession.Lock" />, gated by <paramref name="version" /> (ADR-004 4b).</remarks>
	/// <param name="match">The match whose host to publish.</param>
	/// <param name="version">This call's state version, allocated by the calling <see cref="MatchMutationScope" />.</param>
	/// <param name="cancellationToken">A token that cancels the host lookup.</param>
	public async Task PublishHostAsync(MatchSession match, long version, CancellationToken cancellationToken = default)
	{
		var host = await MatchLiveSnapshotBuilder.BuildHost(match, gameRegistry, ircRegistry, userRepo,
			cancellationToken);
		if (match.HostSnapshot.Publish(host, version) is { } delta)
			eventBus.PublishHost(match.DbId, delta);
	}

	/// <summary>Rebuilds and republishes the referee list snapshot channel.</summary>
	/// <remarks>Runs without holding <see cref="MatchSession.Lock" />, gated by <paramref name="version" /> (ADR-004 4b).</remarks>
	/// <param name="match">The match whose referees to publish.</param>
	/// <param name="version">This call's state version, allocated by the calling <see cref="MatchMutationScope" />.</param>
	/// <param name="cancellationToken">A token that cancels the referee lookups.</param>
	public async Task PublishRefsAsync(MatchSession match, long version, CancellationToken cancellationToken = default)
	{
		var refs = await MatchLiveSnapshotBuilder.BuildRefs(
			match, gameRegistry, ircRegistry, userRepo, cancellationToken);
		if (match.RefsSnapshot.Publish(refs, version) is { } delta)
			eventBus.PublishRefs(match.DbId, delta);
	}

	/// <summary>Rebuilds and republishes the banlist snapshot channel.</summary>
	/// <remarks>Runs without holding <see cref="MatchSession.Lock" />, gated by <paramref name="version" /> (ADR-004 4b).</remarks>
	/// <param name="match">The match whose banlist to publish.</param>
	/// <param name="version">This call's state version, allocated by the calling <see cref="MatchMutationScope" />.</param>
	/// <param name="cancellationToken">A token that cancels the ban lookups.</param>
	public async Task PublishBansAsync(MatchSession match, long version, CancellationToken cancellationToken = default)
	{
		var bans = await MatchLiveSnapshotBuilder.BuildBans(match, gameRegistry, ircRegistry, userRepo,
			cancellationToken);
		if (match.BansSnapshot.Publish(bans, version) is { } delta)
			eventBus.PublishBans(match.DbId, delta);
	}

	/// <summary>Republishes the countdown timer snapshot channel.</summary>
	/// <param name="match">The match whose timer to publish.</param>
	/// <param name="version">This call's state version, allocated by the calling <see cref="MatchMutationScope" />.</param>
	public void PublishTimer(MatchSession match, long version)
	{
		if (match.TimerSnapshot.Publish(MatchLiveSnapshotBuilder.BuildTimerLive(match), version) is { } delta)
			eventBus.PublishTimer(match.DbId, delta);
	}

	/// <summary>
	///     Announces a message into the match's chat channel and, for any referee not already reached
	///     through that channel, as a direct message.
	/// </summary>
	internal void AnnounceToRoomAndReferees(MatchSession match, string text)
	{
		var bot = gameRegistry.GetByUserId(BotBootstrapService.BotId);
		if (bot is null) return;

		EnqueueChat(match, bot.Name, bot.Id, text);

		var channel = channelRegistry.GetByName(match.ChatChannelName);
		foreach (var refereeId in match.Referees)
		{
			if (channel is not null && channel.Contains(refereeId)) continue;
			if (gameRegistry.GetByUserId(refereeId) is { } game)
				game.IrcConnection.Send(IrcMessageWriter.Privmsg(bot.Name, bot.Id, game.Name, text));
			if (ircRegistry.GetByUserId(refereeId) is { } irc)
				irc.IrcConnection.Send(IrcMessageWriter.Privmsg(bot.Name, bot.Id, irc.Name, text));
		}
	}

	/// <summary>Broadcasts a packet to the lobby channel, but only when it is non-empty.</summary>
	/// <param name="data">The serialized packet bytes.</param>
	/// <param name="lobby"><see langword="true" /> to allow the lobby broadcast; otherwise, <see langword="false" />.</param>
	private void BroadcastToNonEmptyLobby(byte[] data, bool lobby)
	{
		if (!lobby) return;

		var lobbyChannel = channelRegistry.GetByName("#lobby");
		if (lobbyChannel is not null && lobbyChannel.PlayerCount > 0)
			channelMembership.BroadcastToMembers(lobbyChannel, data);
	}
}