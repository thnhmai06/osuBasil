using Basil.Domain.Permissions;
using Basil.Domain.Users;

namespace Basil.Application.Runtime.Sessions;

/// <summary>An osu! client as one connection may act on it.</summary>
public sealed class ScopedBanchoConnection : Scoped<Permissions>
{
	/// <summary>Creates a scope for an osu! client and a connection.</summary>
	/// <param name="host">The client to scope.</param>
	/// <param name="by">The connection acting on the client.</param>
	internal ScopedBanchoConnection(BanchoConnection host, Connection by) : base(by, by.User.EffectivePermissions(host.Context.Clock.GetUtcNow()))
	{
		Host = host;
	}

	/// <summary>Gets the osu! client this scope is over.</summary>
	public BanchoConnection Host { get; }

	/// <summary>Starts spectating this client.</summary>
	/// <returns>The outcome, or the permissions the caller lacks.</returns>
	/// <remarks>
	///     Needs <see cref="Permissions.SpectatingWatch" />, which <see cref="Permissions.RoomObserve" /> includes. A
	///     host's block of the spectator prevents spectating unless the spectator has
	///     <see cref="Permissions.RoomObserve" /> in effect. The spectator joins the host's spectator channel, and the
	///     first spectator brings the host into it too. A connection spectating another client stops spectating it first.
	///     Spectating this host again announces the spectator, as an osu! client does once it has the beatmap.
	/// </remarks>
	/// <exception cref="ArgumentException"><see cref="Scoped{TOwner}.By" /> is an IRC connection, which cannot spectate.</exception>
	public async Task<Result<ConnectionOutcome>> SpectateAsync()
	{
		if (By is IrcConnection)
			throw new ArgumentException("IRC clients cannot spectate.");

		if (Require(Permissions.SpectatingWatch) is { } missing) return Result<ConnectionOutcome>.Denied(missing);

		var relationships = await Host.Context.Relationships.ListAsync(Host.Record);
		if (relationships.Any(relationship => relationship.Type == RelationshipType.Block &&
		                                      relationship.Target.Equals(By.Record)) &&
		    Require(Permissions.RoomObserve) is not null)
			return ConnectionOutcome.Blocked;

		// Leave the previous host before entering this host's scope, so two spectator scopes are never held at once.
		var previous = Host.Context.Registry.FindSpectating(By);
		if (previous is not null && !ReferenceEquals(previous, Host)) previous.ScopeTo(By).StopSpectating();

		using var scope = Host.EnterSpectators();

		if (!Host.IsOpen) return ConnectionOutcome.TargetOffline;
		// The connection may have closed during the awaits above; check it again before the spectator is added.
		if (!By.IsOpen) return ConnectionOutcome.Closed;
		if (By.Record.Equals(Host.Record)) return ConnectionOutcome.Self;
		if (Host.Spectators.Contains(By))
		{
			Host.Context.Events.Publish(new UserSpectatorJoined(Host, By));
			return ConnectionOutcome.Ok;
		}

		var first = Host.Spectators.Count == 0;
		Host.MutableSpectators.Add(By);
		if (first) Host.SpectatorChannel.ScopeTo(Host).Join();
		Host.SpectatorChannel.ScopeTo(By).Join();
		Host.Context.Events.Publish(new UserSpectatorJoined(Host, By));
		return ConnectionOutcome.Ok;
	}

	/// <summary>Stops this connection spectating this host.</summary>
	/// <returns><see langword="true" /> if the connection stopped spectating.</returns>
	/// <remarks>Calling it again does nothing.</remarks>
	public bool StopSpectating()
	{
		using var scope = Host.EnterSpectators();
		if (!Host.MutableSpectators.Remove(By)) return false;

		Host.SpectatorChannel.ScopeTo(By).Part();
		if (Host.Spectators.Count == 0) Host.SpectatorChannel.ScopeTo(Host).Part();
		Host.Context.Events.Publish(new UserSpectatorLeft(Host, By));
		return true;
	}

	/// <summary>Reports that this connection failed to spectate this host, for example because it lacks the beatmap.</summary>
	/// <returns><see langword="true" /> if the connection is spectating this host.</returns>
	public bool ReportSpectatingFailed()
	{
		if (!Host.Spectators.Contains(By)) return false;

		Host.Context.Events.Publish(new UserSpectatorFailed(Host, By));
		return true;
	}

	/// <summary>Records the presence status this client reported.</summary>
	/// <param name="status">The reported status.</param>
	/// <returns><see langword="true" /> if the status changed.</returns>
	/// <remarks>A different connection, a closed client, or its current status does nothing.</remarks>
	public bool SetStatus(PlayerStatus status)
	{
		if (!ReferenceEquals(By, Host) || !Host.IsOpen || Equals(Host.Status, status)) return false;

		Host.Status = status;
		Host.Context.Events.Publish(new UserConnectionStatusChanged(Host, status));
		return true;
	}
}
