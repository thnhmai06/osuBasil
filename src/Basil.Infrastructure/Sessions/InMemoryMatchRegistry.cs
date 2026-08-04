using Basil.Application.Sessions.Multiplayer;

namespace Basil.Infrastructure.Sessions;

/// <inheritdoc cref="IMatchRegistry" />
/// <remarks>
///     Backed by a dictionary keyed on the wire-protocol match id, guarded by a single lock.
///     <see cref="GetByDbId" /> and <see cref="All" /> scan the dictionary under the same lock
///     because the database id and the wire-protocol id are unrelated keys.
/// </remarks>
public sealed class InMemoryMatchRegistry : IMatchRegistry
{
	/// <summary>Guards every read and write of <see cref="_matches" />.</summary>
	private readonly Lock _registryLock = new();

	/// <summary>The match sessions by wire-protocol id.</summary>
	private readonly Dictionary<int, MatchSession> _matches = new();

	/// <inheritdoc />
	public MatchSession? GetById(int id)
	{
		lock (_registryLock)
		{
			return _matches.GetValueOrDefault(id);
		}
	}

	/// <inheritdoc />
	/// <remarks>Scans every match until the first whose <see cref="MatchSession.DbId" /> matches.</remarks>
	public MatchSession? GetByDbId(int dbId)
	{
		lock (_registryLock)
		{
			return _matches.Values.FirstOrDefault(m => m.DbId == dbId);
		}
	}

	/// <inheritdoc />
	/// <remarks>Claims the lowest-numbered id not currently in use.</remarks>
	public MatchSession TryCreate(Func<int, MatchSession> factory)
	{
		lock (_registryLock)
		{
			var id = 0;
			while (_matches.ContainsKey(id)) id++;

			var match = factory(id);
			_matches[id] = match;
			return match;
		}
	}

	/// <inheritdoc />
	public void Remove(int id)
	{
		lock (_registryLock)
		{
			_matches.Remove(id);
		}
	}

	/// <inheritdoc />
	public IReadOnlyList<MatchSession> All
	{
		get
		{
			lock (_registryLock)
			{
				return [.. _matches.Values];
			}
		}
	}
}
