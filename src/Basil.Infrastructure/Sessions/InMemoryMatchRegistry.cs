using System.Collections.Concurrent;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Protocol.Multiplayer;

namespace Basil.Infrastructure.Sessions;

/// <inheritdoc cref="IMatchRegistry" />
/// <remarks>
///     Backed by two concurrent dictionaries: one keyed on the wire-protocol match id, one keyed on
///     the persistent database id. <see cref="CreateAsync" /> finds the lowest free wire-protocol id
///     and reserves it via <see cref="ConcurrentDictionary{TKey,TValue}.TryAdd" />, retrying with the
///     next id on a lost race instead of taking a lock; this is safe because building a candidate
///     <see cref="MatchSession" /> has no side effects, so a discarded attempt costs nothing.
/// </remarks>
public sealed class InMemoryMatchRegistry(IChannelRegistry channelRegistry, IMatchRepository matchRepository)
	: IMatchRegistry
{
	private readonly ConcurrentDictionary<int, int> _dbIdtoId = new();
	private readonly ConcurrentDictionary<int, MatchSession> _matches = new();

	/// <inheritdoc />
	public MatchSession? GetById(int id)
	{
		return _matches.GetValueOrDefault(id);
	}

	/// <inheritdoc />
	/// <remarks>Scans every match until the first whose <see cref="MatchSession.DbId" /> matches.</remarks>
	public MatchSession? GetByDbId(int dbId)
	{
		return _dbIdtoId.TryGetValue(dbId, out var protocolId) ? GetById(protocolId) : null;
	}

	/// <inheritdoc />
	/// <remarks>Claims the lowest-numbered id not currently in use.</remarks>
	public async Task<MatchSession> CreateAsync(MatchState data, int hostId,
		CancellationToken cancellationToken = default)
	{
		MatchSession match;
		var id = 0;
		do
		{
			while (_matches.ContainsKey(id)) id++;
			match = BuildNew(id, data, hostId);
		} while (!_matches.TryAdd(id, match));

		match.DbId =
			await matchRepository.CreateMatchAsync(match.Name, DateTimeOffset.UtcNow.UtcDateTime, cancellationToken);
		_dbIdtoId[match.DbId] = match.Id;
		RegisterChannel(match);

		return match;
	}

	/// <inheritdoc />
	public void Remove(int id)
	{
		if (!_matches.TryRemove(id, out var match)) return;
		_dbIdtoId.TryRemove(match.DbId, out _);

		RemoveChannel(match);
		_ = matchRepository.SetMatchEndedAsync(match.DbId, DateTimeOffset.UtcNow.UtcDateTime);
	}

	/// <inheritdoc />
	public IReadOnlyCollection<MatchSession> All => (IReadOnlyCollection<MatchSession>)_matches.Values;

	/// <summary>Constructs the in-memory match session for parsed match-create data.</summary>
	/// <param name="id">The in-memory registry slot id.</param>
	/// <param name="data">The parsed match-create data.</param>
	/// <param name="hostId">The id of the userSession who created the room.</param>
	/// <returns>The fully constructed <see cref="MatchSession" />.</returns>
	private static MatchSession BuildNew(int id, MatchState data, int hostId)
	{
		return new MatchSession(
			id, data.Name, data.Password, data.MapName, data.MapId, data.MapMd5,
			hostId, (GameMode)data.Mode, (Mods)data.Mods, (MatchWinCondition)data.WinCondition,
			(MatchTeamType)data.TeamType, data.FreeMods, data.Seed, ChannelNameFor(id));
	}

	/// <summary>Get the chat channel name of a match.</summary>
	/// <param name="matchId">The match's in-memory registry id.</param>
	/// <returns>The <c>#multi_{id}</c> channel name.</returns>
	private static string ChannelNameFor(int matchId)
	{
		return $"#multi_{matchId}";
	}

	/// <summary>Registers the match's chat channel in the channel registry.</summary>
	/// <param name="match">The match whose channel to register.</param>
	private void RegisterChannel(MatchSession match)
	{
		channelRegistry.Add(new ChannelSession(
			0, match.ChatChannelName, $"MID {match.Id}'s multiplayer channel.",
			0, 0, false, "#multiplayer", true));
	}

	/// <summary>Registers the match's chat channel in the channel registry.</summary>
	/// <param name="match">The match whose channel to register.</param>
	private void RemoveChannel(MatchSession match)
	{
		channelRegistry.Remove(match.ChatChannelName);
	}
}