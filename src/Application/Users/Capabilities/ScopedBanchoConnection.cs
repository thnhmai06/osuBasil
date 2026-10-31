using Basil.Application.Common;
using Basil.Application.Common.Common;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Objects.Events;
using Basil.Application.Users.Registry;
using Basil.Domain.Client;
using Basil.Domain.Users;

namespace Basil.Application.Users.Capabilities;

/// <summary>An osu! client as one connection may act on it.</summary>
public sealed class ScopedBanchoConnection : Scoped<UserPermissions, Connection>
{
	/// <summary>Creates a scope for an osu! client and a connection.</summary>
	/// <param name="host">The client to scope.</param>
	/// <param name="by">The connection acting on the client.</param>
	internal ScopedBanchoConnection(BanchoConnection host, Connection by) : base(by,
		by.EffectivePermissions(host.Context.Clock.GetUtcNow()))
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
	/// <exception cref="ArgumentException"><see cref="Scoped{TPermissions,TActor}.By" /> is an IRC connection, which cannot spectate.</exception>
	public async Task<Result<ConnectionOutcome>> SpectateAsync()
	{
		if (By is IrcConnection)
			throw new ArgumentException("IRC clients cannot spectate.");

		if (Require(UserPermissions.SpectatingWatch) is { } missing) return Result<ConnectionOutcome>.Denied(missing);

		if (await IsBlockedAsync()) return ConnectionOutcome.Blocked;

		// Leave the previous host before entering this host's scope, so two spectator scopes are never held at once.
		LeavePreviousHost();

		using var scope = Host.EnterSpectators();

		if (SpectatorRefusal() is { } refusal) return refusal;
		if (Host.Spectators.Contains(By))
		{
			Host.Context.Events.Publish(new UserSpectatorJoined(Host, By));
			return ConnectionOutcome.Ok;
		}

		Host.MutableSpectators.Add(By);
		Host.Context.Events.Publish(new UserSpectatorJoined(Host, By));
		return ConnectionOutcome.Ok;
	}

	private async Task<bool> IsBlockedAsync()
	{
		var relationships = await Host.Context.Relationships.ListAsync(Host.Record);
		return relationships.Any(relationship => relationship.Type == RelationshipType.Block &&
		                                         relationship.Target.Equals(By.Record)) &&
		       Require(UserPermissions.RoomObserve) is not null;
	}

	private void LeavePreviousHost()
	{
		var previous = Host.Context.Registry.FindSpectating(By);
		if (previous is not null && !ReferenceEquals(previous, Host)) previous.ScopeTo(By).StopSpectating();
	}

	private ConnectionOutcome? SpectatorRefusal()
	{
		if (!Host.IsOpen) return ConnectionOutcome.TargetOffline;
		// The connection may have closed during the awaits above; check it again before the spectator is added.
		if (!By.IsOpen) return ConnectionOutcome.Closed;
		if (By.Record.Equals(Host.Record)) return ConnectionOutcome.Self;
		return null;
	}

	/// <summary>Stops this connection spectating this host.</summary>
	/// <returns><see langword="true" /> if the connection stopped spectating.</returns>
	/// <remarks>Calling it again does nothing.</remarks>
	public bool StopSpectating()
	{
		using var scope = Host.EnterSpectators();
		if (!Host.MutableSpectators.Remove(By)) return false;

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

	/// <summary>Hands the frames this client sent to the HTTP API connections spectating it.</summary>
	/// <param name="bundle">The decoded frames.</param>
	/// <returns><see langword="true" /> if the frames were published; only the client itself may, and only while an API connection spectates it.</returns>
	public bool PublishFrames(SpectatorBundle bundle)
	{
		if (!ReferenceEquals(By, Host) || !Host.IsOpen || !Host.HasApiSpectators) return false;

		Host.Context.Events.Publish(new UserSpectatorFramesReceived(Host, bundle));
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