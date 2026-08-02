using Basil.Application.Sessions.Multiplayer;

namespace Basil.Infrastructure.Sessions;

/// <inheritdoc cref="IMatchRegistry" />
/// <remarks>
///     A fixed 64-element array of <see cref="MatchSession" /> references guarded by a single lock.
///     The array index is the wire-protocol slot id, so ids outside 0 to 63 are rejected rather
///     than stored. <see cref="GetByDbId" /> and <see cref="All" /> scan the array under the same
///     lock because the database id and the slot id are unrelated keys.
/// </remarks>
public sealed class InMemoryMatchRegistry : IMatchRegistry
{
	/// <summary>Guards every read and write of <see cref="_slots" />.</summary>
	private readonly Lock _registryLock = new();

	/// <summary>The match sessions by wire-protocol slot id, with null for free slots.</summary>
	private readonly MatchSession?[] _slots = new MatchSession?[IMatchRegistry.MaxMatches];

	/// <inheritdoc />
	/// <remarks>Ids outside the 0 to 63 slot range return null without touching the lock.</remarks>
	public MatchSession? GetById(int id)
	{
		if (id is < 0 or >= IMatchRegistry.MaxMatches) return null;

		lock (_registryLock)
		{
			return _slots[id];
		}
	}

	/// <inheritdoc />
	/// <remarks>Scans every slot until the first session whose <see cref="MatchSession.DbId" /> matches.</remarks>
	public MatchSession? GetByDbId(int dbId)
	{
		lock (_registryLock)
		{
			return _slots.FirstOrDefault(m => m is not null && m.DbId == dbId);
		}
	}

	/// <inheritdoc />
	/// <remarks>Claims the lowest-numbered free slot.</remarks>
	public MatchSession? TryCreate(Func<int, MatchSession> factory)
	{
		lock (_registryLock)
		{
			for (var i = 0; i < IMatchRegistry.MaxMatches; i++)
				if (_slots[i] is null)
				{
					var match = factory(i);
					_slots[i] = match;
					return match;
				}

			return null;
		}
	}

	/// <inheritdoc />
	/// <remarks>Ids outside the 0 to 63 slot range are ignored.</remarks>
	public void Remove(int id)
	{
		if (id is < 0 or >= IMatchRegistry.MaxMatches) return;

		lock (_registryLock)
		{
			_slots[id] = null;
		}
	}

	/// <inheritdoc />
	public IReadOnlyList<MatchSession> All
	{
		get
		{
			lock (_registryLock)
			{
				return [.. _slots.Where(m => m is not null).Cast<MatchSession>()];
			}
		}
	}
}