using Basil.Application.Common;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Objects.Events;
using Basil.Domain.Client;
using Basil.Domain.Users;

namespace Basil.Application.Users.Capabilities;

/// <summary>An osu! client as one connection may act on it.</summary>
public sealed class ScopedBanchoConnection : Scoped<UserPermissions, Connection>
{
	/// <summary>Creates a scope for an osu! client and a connection.</summary>
	/// <param name="host">The client to scope.</param>
	/// <param name="actor">The connection acting on the client.</param>
	internal ScopedBanchoConnection(BanchoConnection host, Connection actor) : base(actor,
		actor.EffectivePermissions(host.Context.Clock.GetUtcNow()))
	{
		Host = host;
	}

	/// <summary>Gets the osu! client this scope is over.</summary>
	public BanchoConnection Host { get; }

	/// <summary>Starts spectating this client.</summary>
	/// <returns>The outcome, or the permissions the caller lacks.</returns>
	/// <remarks>
	///     Needs <see cref="UserPermissions.SpectatingWatch" />, which <see cref="UserPermissions.RoomObserve" /> includes. A
	///     host's block of the spectator prevents spectating unless the spectator has
	///     <see cref="UserPermissions.RoomObserve" /> in effect. The spectator joins the host's spectator channel, and the
	///     first spectator brings the host into it too. A connection spectating another client stops spectating it first.
	///     Spectating this host again announces the spectator, as an osu! client does once it has the beatmap.
	/// </remarks>
	/// <exception cref="ArgumentException">
	///     <see cref="Scoped{TPermissions,TActor}.Actor" /> is an IRC connection, which cannot
	///     spectate.
	/// </exception>
	public async Task<Result<ConnectionOutcome>> SpectateAsync()
	{
		if (Actor is IrcConnection)
			throw new ArgumentException("IRC clients cannot spectate.");

		if (Require(UserPermissions.SpectatingWatch) is { } missing) return Result<ConnectionOutcome>.Denied(missing);

		if (await IsBlockedAsync()) return ConnectionOutcome.Blocked;

		// Leave the previous host before entering this host's scope, so two spectator scopes are never held at once.
		LeavePreviousHost();

		using var scope = Host.EnterSpectators();

		if (SpectatorRefusal() is { } refusal) return refusal;
		if (Host.Spectators.Contains(Actor))
		{
			Host.Context.Events.Publish(new UserSpectatorJoined(Host, Actor));
			return ConnectionOutcome.Ok;
		}

		Host.MutableSpectators.Add(Actor);
		Host.Context.Events.Publish(new UserSpectatorJoined(Host, Actor));
		return ConnectionOutcome.Ok;
	}

	private async Task<bool> IsBlockedAsync()
	{
		var relationships = await Host.Context.Relationships.ListAsync(Host.Record);
		return relationships.Any(relationship => relationship.Type == RelationshipType.Block &&
		                                         relationship.Target.Equals(Actor.Record)) &&
		       Require(UserPermissions.RoomObserve) is not null;
	}

	private void LeavePreviousHost()
	{
		var previous = Host.Context.Registry.FindSpectating(Actor);
		if (previous is not null && !ReferenceEquals(previous, Host)) previous.ScopeTo(Actor).StopSpectating();
	}

	private ConnectionOutcome? SpectatorRefusal()
	{
		if (!Host.IsOpen) return ConnectionOutcome.TargetOffline;
		// The connection may have closed during the awaits above; check it again before the spectator is added.
		if (!Actor.IsOpen) return ConnectionOutcome.Closed;
		if (Actor.Record.Equals(Host.Record)) return ConnectionOutcome.Self;
		return null;
	}

	/// <summary>Stops this connection spectating this host.</summary>
	/// <returns><see langword="true" /> if the connection stopped spectating.</returns>
	/// <remarks>Calling it again does nothing.</remarks>
	public bool StopSpectating()
	{
		using var scope = Host.EnterSpectators();
		if (!Host.MutableSpectators.Remove(Actor)) return false;

		Host.Context.Events.Publish(new UserSpectatorLeft(Host, Actor));
		return true;
	}

	/// <summary>Reports that this connection failed to spectate this host, for example because it lacks the beatmap.</summary>
	/// <returns><see langword="true" /> if the connection is spectating this host.</returns>
	public bool ReportSpectatingFailed()
	{
		if (!Host.Spectators.Contains(Actor)) return false;

		Host.Context.Events.Publish(new UserSpectatorFailed(Host, Actor));
		return true;
	}

	/// <summary>Hands the frames this client sent to the HTTP API connections spectating it.</summary>
	/// <param name="bundle">The decoded frames.</param>
	/// <returns>
	///     <see langword="true" /> if the frames were published; only the client itself may, and only while an API
	///     connection spectates it.
	/// </returns>
	public bool PublishFrames(SpectatorBundle bundle)
	{
		if (!ReferenceEquals(Actor, Host) || !Host.IsOpen || !Host.HasApiSpectators) return false;

		Host.Context.Events.Publish(new UserSpectatorFramesReceived(Host, bundle));
		return true;
	}

	/// <summary>Records the presence status this client reported.</summary>
	/// <param name="status">The reported status.</param>
	/// <returns><see langword="true" /> if the status changed.</returns>
	/// <remarks>A different connection, a closed client, or its current status does nothing.</remarks>
	public bool SetStatus(PlayerStatus status)
	{
		if (!ReferenceEquals(Actor, Host) || !Host.IsOpen || Equals(Host.Status, status)) return false;

		Host.Status = status;
		Host.Context.Events.Publish(new UserConnectionStatusChanged(Host, status));
		return true;
	}
}