using System.Collections.Concurrent;
using Basil.Domain.Client;
using Basil.Domain.Social;

namespace Basil.Application.Models.Sessions;

/// <summary>
///     A real osu! client's session: gameplay presence, spectating, and current room membership, in
///     addition to the chat state every <see cref="UserSession" /> has.
/// </summary>
/// <remarks>
///     Thread-safe with respect to spectator bookkeeping: another session starting or stopping
///     spectating this one does not corrupt <see cref="SpectatorIds" />.
/// </remarks>
public sealed class GameSession : UserSession
{
	private readonly ConcurrentDictionary<int, byte> _spectatorIds = new();

	/// <summary>Gets the osu! client version reported at login.</summary>
	public required ClientVersion ClientVersion { get; init; }

	/// <summary>Gets the hardware and client fingerprint captured at login.</summary>
	public required ClientFingerprint ClientFingerprint { get; init; }

	/// <summary>Gets the client's UTC offset reported at login.</summary>
	public int UtcOffset { get; init; }

	/// <summary>Gets or sets the presence-list filter the client requested.</summary>
	public PresenceVisibility PresenceVisibility { get; set; } = PresenceVisibility.Nil;

	/// <summary>Gets the client's currently reported presence status.</summary>
	public PlayerStatus Status { get; private set; } = PlayerStatus.Idle;

	/// <summary>Gets the id of the session this client is currently spectating, or <see langword="null" /> if none.</summary>
	public int? SpectatingUserId { get; private set; }

	/// <summary>Gets the ids of the sessions currently spectating this client.</summary>
	public IReadOnlyCollection<int> SpectatorIds => _spectatorIds.Keys.ToArray();

	/// <summary>Gets the id of the room this client is currently in, or <see langword="null" /> if none.</summary>
	public int? RoomId { get; set; }

	/// <summary>Changes this session's reported presence status.</summary>
	/// <param name="status">The new status to apply.</param>
	public void ChangeStatus(PlayerStatus status)
	{
		Status = status;
		Record(new StatusChanged(this));
	}

	/// <summary>Starts spectating another client, keeping both sides of the relationship consistent.</summary>
	/// <param name="host">The session to spectate.</param>
	/// <exception cref="InvalidOperationException">
	///     <paramref name="host" /> is this same session, or this session is already spectating someone.
	/// </exception>
	public void StartSpectating(GameSession host)
	{
		if (host.UserId == UserId)
			throw new InvalidOperationException("A session cannot spectate itself.");
		if (SpectatingUserId is not null)
			throw new InvalidOperationException("This session is already spectating someone.");

		SpectatingUserId = host.UserId;
		host._spectatorIds[UserId] = 0;
		Record(new SpectateStarted(this, host));
	}

	/// <summary>Stops spectating, keeping both sides of the relationship consistent.</summary>
	/// <param name="host">The session currently being spectated, matching <see cref="SpectatingUserId" />.</param>
	/// <exception cref="InvalidOperationException">This session is not spectating <paramref name="host" />.</exception>
	public void StopSpectating(GameSession host)
	{
		if (SpectatingUserId != host.UserId)
			throw new InvalidOperationException("This session is not spectating the given host.");

		SpectatingUserId = null;
		host._spectatorIds.TryRemove(UserId, out _);
		Record(new SpectateStopped(this, host));
	}
}