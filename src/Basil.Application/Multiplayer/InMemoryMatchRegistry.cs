using System.Collections.Concurrent;
using Basil.Application.Beatmaps;
using Basil.Application.Channels;
using Basil.Domain.Beatmaps;
using Basil.Domain.Channels;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer;

/// <inheritdoc cref="IMatchRegistry" />
/// <remarks>
///     Stores matches keyed by both the wire-protocol match id and the persistent database id.
///     <see cref="CreateAsync" /> assigns the lowest free wire-protocol id, retrying with the next
///     id when a concurrent creation wins the race for the candidate id.
/// </remarks>
public sealed class InMemoryMatchRegistry(
	IChannelRegistry channelRegistry,
	IMatchRepository matchRepository,
	IBeatmapRepository beatmapRepository)
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
	public async Task<MatchSession> CreateAsync(MatchCreationData data, User? host,
		CancellationToken cancellationToken = default)
	{
		// The persistent id is claimed first because it names the room's chat channel, which is fixed
		// for the session's lifetime.
		var dbId =
			await matchRepository.CreateMatchAsync(data.Name, DateTimeOffset.UtcNow.UtcDateTime, cancellationToken);

		// The creation request's claimed beatmap may not resolve locally (an unranked or not-yet-seen
		// map) -- that is an expected state, not an error, so a miss just leaves the room's beatmap
		// unset rather than failing the whole creation.
		var beatmap = data.MapId is { } mapId
			? await beatmapRepository.FetchOneAsync(mapId, cancellationToken: cancellationToken)
			: null;

		MatchSession match;
		var id = 0;
		do
		{
			while (_matches.ContainsKey(id)) id++;
			match = BuildNew(id, dbId, data, beatmap, host);
		} while (!_matches.TryAdd(id, match));

		_dbIdtoId[dbId] = match.Id;
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
	/// <param name="dbId">The match's persistent database id.</param>
	/// <param name="data">The match-create settings.</param>
	/// <param name="beatmap">
	///     The initially selected beatmap, resolved from <see cref="MatchCreationData.MapId" />, or null
	///     when it does not resolve locally.
	/// </param>
	/// <param name="host">The userSession who created the room, or <see langword="null" /> when nobody holds gameplay host.</param>
	/// <returns>The fully constructed <see cref="MatchSession" />.</returns>
	private static MatchSession BuildNew(int id, int dbId, MatchCreationData data, Beatmap? beatmap, User? host)
	{
		return new MatchSession(
			id, data.Name, data.Password, beatmap,
			host, data.Mode, data.Mods, data.WinCondition,
			data.TeamType, data.FreeMods, data.Seed)
		{
			DbId = dbId
		};
	}

	/// <summary>Registers the match's chat channel in the channel registry.</summary>
	/// <param name="match">The match whose channel to register.</param>
	private void RegisterChannel(MatchSession match)
	{
		channelRegistry.Add(new ChannelSession(
			0, match.ChatChannelName,
			0, 0, false, "#multiplayer", true)
		{
			Topic = match.Name
		});
	}

	/// <summary>Registers the match's chat channel in the channel registry.</summary>
	/// <param name="match">The match whose channel to register.</param>
	private void RemoveChannel(MatchSession match)
	{
		channelRegistry.Remove(match.ChatChannelName);
	}
}